using DRC.Api.Data;
using DRC.Api.Data.Entities;
using DRC.Api.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DRC.Api.Controllers
{
    /// <summary>
    /// Public, idempotent emergency-report endpoint used by the Progressive Web App.
    ///
    /// Designed for offline-tolerant operation: the PWA service worker queues SOS
    /// reports in IndexedDB while the device is offline and replays them through
    /// this endpoint when connectivity returns. Idempotency is keyed on
    /// <c>clientId</c> (a client-generated GUID) so the same incident is never
    /// duplicated even if the SW replays a request multiple times.
    ///
    /// No authentication required — any citizen on the open web can hit /api/sos
    /// from the installed PWA.
    /// </summary>
    [ApiController]
    [Route("api/sos")]
    [AllowAnonymous]
    public class SosController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IAgentService _agent;
        private readonly DRC.Api.Services.ILiveNotifier _live;
        private readonly ILogger<SosController> _logger;

        public SosController(
            ApplicationDbContext db,
            IAgentService agent,
            DRC.Api.Services.ILiveNotifier live,
            ILogger<SosController> logger)
        {
            _db = db;
            _agent = agent;
            _live = live;
            _logger = logger;
        }

        public class SosRequest
        {
            /// <summary>Client-generated GUID used to deduplicate SW replays.</summary>
            public Guid ClientId { get; set; }
            /// <summary>Fire | Medical | Flood | Landslide | SOS</summary>
            public string Type { get; set; } = "SOS";
            public string? Severity { get; set; }
            public string? Location { get; set; }
            public double? Latitude { get; set; }
            public double? Longitude { get; set; }
            public string? Description { get; set; }
            public string? Phone { get; set; }
            /// <summary>ISO timestamp of when the user actually pressed SOS (may be earlier than server receipt).</summary>
            public DateTime? OccurredAt { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> Submit([FromBody] SosRequest req)
        {
            if (req == null) return BadRequest(new { error = "missing body" });
            if (req.ClientId == Guid.Empty) req.ClientId = Guid.NewGuid();

            // Idempotency — if we've already stored this clientId, return the stored record.
            var existing = await _db.EmergencyRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RequestGuid == req.ClientId);
            if (existing != null)
            {
                _logger.LogInformation("📥 SOS replay accepted (already stored) id={Id} client={Cid}",
                    existing.Id, req.ClientId);
                return Ok(new
                {
                    status = "duplicate",
                    id = existing.Id,
                    requestGuid = existing.RequestGuid,
                    queuedFor = (existing.CreatedAt - (req.OccurredAt ?? existing.CreatedAt)).TotalSeconds
                });
            }

            var (type, label) = MapType(req.Type);
            var severity = MapSeverity(req.Severity);
            var location = string.IsNullOrWhiteSpace(req.Location)
                ? (req.Latitude.HasValue && req.Longitude.HasValue
                    ? $"{req.Latitude:F5},{req.Longitude:F5}"
                    : "Location not provided")
                : req.Location!;

            var occurred = req.OccurredAt ?? DateTime.UtcNow;
            var queuedSeconds = (DateTime.UtcNow - occurred).TotalSeconds;
            var queuedTag = queuedSeconds > 30
                ? $" (queued offline for {(int)queuedSeconds}s)"
                : "";

            var entity = new EmergencyRequest
            {
                RequestGuid = req.ClientId,
                Type = type,
                Severity = severity,
                Status = RequestStatus.Pending,
                Description = $"[PWA] {label} reported{queuedTag}. " +
                              (string.IsNullOrWhiteSpace(req.Description) ? "" : $"Details: {req.Description}. ") +
                              $"Reported at {occurred:O}.",
                Location = location,
                Latitude = req.Latitude,
                Longitude = req.Longitude,
                UserPhone = NormalizePhone(req.Phone),
                CreatedAt = DateTime.UtcNow
            };

            _db.EmergencyRequests.Add(entity);
            await _db.SaveChangesAsync();

            _logger.LogInformation("🚨 PWA SOS #{Id} type={Type} sev={Sev} loc={Loc} client={Cid}{Tag}",
                entity.Id, type, severity, location, req.ClientId, queuedTag);

            // Live push to admin dashboard
            try
            {
                await _live.EmergencyCreatedAsync(new
                {
                    id = entity.Id,
                    type = entity.Type.ToString(),
                    severity = entity.Severity.ToString(),
                    description = entity.Description,
                    location = entity.Location,
                    latitude = entity.Latitude,
                    longitude = entity.Longitude,
                    phone = entity.UserPhone,
                    channel = "PWA",
                    createdAt = entity.CreatedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR push failed for PWA SOS #{Id}", entity.Id);
            }

            // Fire-and-forget: kick the AI agent so the existing pipeline notifies
            // Next of Kin (SMS + WhatsApp + email) using demo defaults if guest.
            _ = Task.Run(async () =>
            {
                try
                {
                    var agentMsg =
                        $"{label} emergency reported via PWA SOS button at {location}. " +
                        (string.IsNullOrWhiteSpace(req.Description) ? "" : $"User note: {req.Description}. ") +
                        "Please dispatch help and notify next of kin.";
                    await _agent.ProcessMessageAsync(
                        sessionId: null,
                        message: agentMsg,
                        userPhone: NormalizePhone(req.Phone),
                        userId: null,
                        latitude: req.Latitude,
                        longitude: req.Longitude);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Agent escalation after PWA SOS failed for #{Id}", entity.Id);
                }
            });

            return Ok(new
            {
                status = "accepted",
                id = entity.Id,
                requestGuid = entity.RequestGuid,
                type = type.ToString(),
                severity = severity.ToString(),
                queuedFor = queuedSeconds
            });
        }

        // --------------------------------------------------------------
        private static (EmergencyType, string label) MapType(string? raw)
        {
            return (raw ?? "").Trim().ToUpperInvariant() switch
            {
                "FIRE"                                  => (EmergencyType.Fire,       "Fire"),
                "MED" or "MEDICAL"                      => (EmergencyType.Medical,    "Medical"),
                "FLOOD"                                 => (EmergencyType.Flood,      "Flood"),
                "LAND" or "LANDSLIDE" or "EARTHQUAKE"   => (EmergencyType.Earthquake, "Landslide"),
                _                                       => (EmergencyType.Other,     "SOS")
            };
        }

        private static EmergencySeverity MapSeverity(string? raw) =>
            (raw ?? "").Trim().ToUpperInvariant() switch
            {
                "LOW"      => EmergencySeverity.Low,
                "MEDIUM"   => EmergencySeverity.Medium,
                "HIGH"     => EmergencySeverity.High,
                "CRITICAL" => EmergencySeverity.Critical,
                _          => EmergencySeverity.High
            };

        private static string? NormalizePhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            phone = phone.Trim();
            if (!phone.StartsWith("+")) phone = "+" + phone.TrimStart('0');
            return phone;
        }
    }
}
