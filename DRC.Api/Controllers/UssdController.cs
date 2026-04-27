using System.Text;
using DRC.Api.Data;
using DRC.Api.Data.Entities;
using DRC.Api.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace DRC.Api.Controllers
{
    /// <summary>
    /// Africa's Talking USSD gateway endpoint.
    /// Lets feature-phone users (no smartphone, no data) report emergencies,
    /// find the nearest facility, register for shelter, or request evacuation.
    ///
    /// Configure the AT dashboard callback to: POST {API_BASE}/api/ussd
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class UssdController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IAgentService _agent;
        private readonly ISmsService _sms;
        private readonly IDistributedCache _cache;
        private readonly DRC.Api.Services.ILiveNotifier _live;
        private readonly ILogger<UssdController> _logger;

        public UssdController(
            ApplicationDbContext db,
            IAgentService agent,
            ISmsService sms,
            IDistributedCache cache,
            DRC.Api.Services.ILiveNotifier live,
            ILogger<UssdController> logger)
        {
            _db = db;
            _agent = agent;
            _sms = sms;
            _cache = cache;
            _live = live;
            _logger = logger;
        }

        /// <summary>
        /// Africa's Talking sends every USSD keypress here.
        /// Response MUST start with "CON " (continue) or "END " (final).
        /// </summary>
        [HttpPost]
        [Consumes("application/x-www-form-urlencoded", "application/json", "text/plain")]
        public async Task<IActionResult> Handle(
            [FromForm] string? sessionId,
            [FromForm] string? serviceCode,
            [FromForm] string? phoneNumber,
            [FromForm] string? text)
        {
            sessionId ??= Guid.NewGuid().ToString("N");
            phoneNumber ??= "";
            text ??= "";

            _logger.LogInformation("📞 USSD {Session} phone={Phone} text='{Text}'", sessionId, phoneNumber, text);

            // AT sends the full input history joined by '*'. The last segment is the latest press.
            var steps = text.Split('*', StringSplitOptions.None);
            var response = await RouteAsync(sessionId, phoneNumber, steps);

            return Content(response, "text/plain", Encoding.UTF8);
        }

        // -------------------------------------------------------------------
        // Routing
        // -------------------------------------------------------------------
        private async Task<string> RouteAsync(string sessionId, string phone, string[] steps)
        {
            // Root menu
            if (steps.Length == 0 || (steps.Length == 1 && string.IsNullOrEmpty(steps[0])))
            {
                return Con(
                    "🛡 DIRECO Emergency",
                    "1. 🚨 Report Emergency",
                    "2. 🏥 Nearest Facility",
                    "3. 🏠 Register for Shelter",
                    "4. 🚐 Request Evacuation",
                    "5. 📞 Emergency Hotlines",
                    "6. ℹ About DIRECO");
            }

            var root = steps[0];

            return root switch
            {
                "1" => await EmergencyFlowAsync(sessionId, phone, steps),
                "2" => await FacilityFlowAsync(steps),
                "3" => await ShelterFlowAsync(sessionId, phone, steps),
                "4" => await EvacuationFlowAsync(sessionId, phone, steps),
                "5" => HotlinesMenu(),
                "6" => AboutMenu(),
                _   => End("Invalid option. Dial *384*10000# to try again.")
            };
        }

        // -------------------------------------------------------------------
        // 1) Report Emergency → type → severity → confirm
        // -------------------------------------------------------------------
        private async Task<string> EmergencyFlowAsync(string sessionId, string phone, string[] steps)
        {
            // steps[0] = "1"
            if (steps.Length == 1)
            {
                return Con("What is happening?",
                    "1. 🔥 Fire",
                    "2. 🏥 Medical Emergency",
                    "3. 🌊 Flood / Landslide",
                    "4. Other");
            }

            if (steps.Length == 2)
            {
                return Con("How severe is it?",
                    "1. Critical — lives at risk",
                    "2. High — serious, need help fast",
                    "3. Medium — need assistance",
                    "4. Low — advisory only");
            }

            if (steps.Length == 3)
            {
                return Con("How many people affected?",
                    "1. Just me",
                    "2. 2-5 people",
                    "3. 6-20 people",
                    "4. More than 20");
            }

            if (steps.Length == 4)
            {
                return Con("Enter a short location description",
                    "(e.g. Bududa Village, near the church)",
                    "Or 0 to skip");
            }

            if (steps.Length >= 5)
            {
                // All data collected — create the request
                var typeCode = steps[1];
                var sevCode  = steps[2];
                var peopleCode = steps[3];
                var location = steps[4] == "0" ? "Location not provided via USSD" : steps[4];

                var (type, label) = MapEmergencyType(typeCode);
                var severity = MapSeverity(sevCode);
                var affected = MapAffected(peopleCode);

                var request = new EmergencyRequest
                {
                    Type = type,
                    Severity = severity,
                    Status = RequestStatus.Pending,
                    Description = $"[USSD] {label} reported. Approx {affected} people affected. Location: {location}",
                    Location = location,
                    UserPhone = NormalizePhone(phone),
                    CreatedAt = DateTime.UtcNow
                };

                try
                {
                    _db.EmergencyRequests.Add(request);
                    await _db.SaveChangesAsync();

                    // 🔴 Live push to admin dashboard
                    await _live.EmergencyCreatedAsync(new
                    {
                        id = request.Id,
                        type = request.Type.ToString(),
                        severity = request.Severity.ToString(),
                        description = request.Description,
                        location = request.Location,
                        phone = request.UserPhone,
                        channel = "USSD",
                        createdAt = request.CreatedAt
                    });

                    // Fire-and-forget SMS confirmation (don't block USSD response)
                    _ = Task.Run(async () =>
                    {
                        var msg = $"DIRECO: Your {label} report (#{request.Id}) has been received. " +
                                  $"Severity: {severity}. Help is being coordinated. " +
                                  $"Call 999 if life is at risk.";
                        try { await _sms.SendSmsAsync(phone, msg); }
                        catch (Exception ex) { _logger.LogWarning(ex, "USSD SMS confirm failed"); }
                    });

                    _logger.LogInformation("🚨 USSD emergency #{Id} created for {Phone}", request.Id, phone);

                    return End(
                        $"✅ Report #{request.Id} received.",
                        $"Type: {label}",
                        $"Severity: {severity}",
                        "An SMS confirmation is on the way.",
                        "Call 999 if life is at risk.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "USSD emergency save failed");
                    return End("Sorry, we could not save your report. Please call 999.");
                }
            }

            return End("Session ended.");
        }

        // -------------------------------------------------------------------
        // 2) Nearest facility (by simple type selection)
        // -------------------------------------------------------------------
        private async Task<string> FacilityFlowAsync(string[] steps)
        {
            if (steps.Length == 1)
            {
                return Con("Find the nearest:",
                    "1. 🏥 Hospital",
                    "2. 🏠 Shelter",
                    "3. 🚓 Police Station",
                    "4. 🚒 Fire Station",
                    "5. 💧 Water Point");
            }

            if (steps.Length == 2)
            {
                var type = steps[1] switch
                {
                    "1" => FacilityType.Hospital,
                    "2" => FacilityType.Shelter,
                    "3" => FacilityType.PoliceStation,
                    "4" => FacilityType.FireStation,
                    "5" => FacilityType.WaterPoint,
                    _   => FacilityType.Hospital
                };

                var list = await _db.Facilities
                    .Where(f => f.Type == type && f.IsOperational)
                    .OrderBy(f => f.Name)
                    .Take(3)
                    .ToListAsync();

                if (list.Count == 0)
                {
                    return End("No operational facility of that type is listed yet.");
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Nearest {type}:");
                foreach (var f in list)
                {
                    sb.AppendLine($"• {Truncate(f.Name, 28)}");
                    if (!string.IsNullOrWhiteSpace(f.Phone)) sb.AppendLine($"  ☎ {f.Phone}");
                    if (!string.IsNullOrWhiteSpace(f.Address)) sb.AppendLine($"  📍 {Truncate(f.Address, 28)}");
                }
                return End(sb.ToString().TrimEnd());
            }

            return End("Session ended.");
        }

        // -------------------------------------------------------------------
        // 3) Shelter registration — family size → location → confirm
        // -------------------------------------------------------------------
        private async Task<string> ShelterFlowAsync(string sessionId, string phone, string[] steps)
        {
            if (steps.Length == 1)
                return Con("Shelter registration", "How many people (incl. you)?");

            if (steps.Length == 2)
                return Con("Which area are you coming from?", "(e.g. Kasese, near trading centre)");

            if (steps.Length >= 3)
            {
                if (!int.TryParse(steps[1], out var size) || size <= 0 || size > 99)
                    return End("Invalid number. Please dial again.");

                var reg = new ShelterRegistration
                {
                    FamilySize = size,
                    Adults = size,
                    Phone = NormalizePhone(phone),
                    Status = RegistrationStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    Notes = $"[USSD] Origin: {steps[2]}"
                };

                _db.ShelterRegistrations.Add(reg);
                await _db.SaveChangesAsync();

                _ = Task.Run(async () =>
                {
                    var msg = $"DIRECO: Shelter registration #{reg.Id} received for {size} person(s) from {steps[2]}. We will SMS your assignment shortly.";
                    try { await _sms.SendSmsAsync(phone, msg); } catch { /* best effort */ }
                });

                return End($"✅ Registration #{reg.Id} received for {size} person(s).",
                    "We will SMS you the assigned shelter.");
            }

            return End("Session ended.");
        }

        // -------------------------------------------------------------------
        // 4) Evacuation request — people count → urgency → location
        // -------------------------------------------------------------------
        private async Task<string> EvacuationFlowAsync(string sessionId, string phone, string[] steps)
        {
            if (steps.Length == 1)
                return Con("Evacuation request", "How many people to evacuate?");

            if (steps.Length == 2)
                return Con("Urgency?",
                    "1. Immediate (now)",
                    "2. Within 1 hour",
                    "3. Today");

            if (steps.Length == 3)
                return Con("Pickup location?", "(short description)");

            if (steps.Length >= 4)
            {
                if (!int.TryParse(steps[1], out var count) || count <= 0 || count > 500)
                    return End("Invalid count. Please dial again.");

                var priority = steps[2] switch
                {
                    "1" => EvacuationPriority.Critical,
                    "2" => EvacuationPriority.High,
                    _   => EvacuationPriority.Normal
                };

                var ev = new EvacuationRequest
                {
                    NumberOfPeople = count,
                    Priority = priority,
                    Status = EvacuationStatus.Requested,
                    PickupLocation = steps[3],
                    Phone = NormalizePhone(phone),
                    CreatedAt = DateTime.UtcNow,
                    Notes = "[USSD] Requested via USSD"
                };

                _db.EvacuationRequests.Add(ev);
                await _db.SaveChangesAsync();

                _ = Task.Run(async () =>
                {
                    var msg = $"DIRECO: Evacuation #{ev.Id} logged ({count} person(s), priority {priority}). A coordinator will call you.";
                    try { await _sms.SendSmsAsync(phone, msg); } catch { /* best effort */ }
                });

                return End($"✅ Evacuation #{ev.Id} requested.",
                    $"{count} person(s) — priority {priority}",
                    "A coordinator will contact you by phone.");
            }

            return End("Session ended.");
        }

        // -------------------------------------------------------------------
        // 5) Hotlines
        // -------------------------------------------------------------------
        private string HotlinesMenu() => End(
            "Uganda Emergency Hotlines:",
            "Police: 999",
            "Ambulance: 911",
            "Fire: 112",
            "Red Cross: 0414 258701",
            "Gender Violence: 0800 277777");

        // -------------------------------------------------------------------
        // 6) About
        // -------------------------------------------------------------------
        private string AboutMenu() => End(
            "DIRECO — AI-powered disaster",
            "response coordinator for Uganda.",
            "Works offline via USSD & SMS",
            "(text FIRE/MED/FLOOD/LAND <place>),",
            "plus WhatsApp & web.",
            "AI Fest 2026.");

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------
        private static string Con(params string[] lines) => "CON " + string.Join("\n", lines);
        private static string End(params string[] lines) => "END " + string.Join("\n", lines);

        private static (EmergencyType type, string label) MapEmergencyType(string code) => code switch
        {
            "1" => (EmergencyType.Fire,       "Fire"),
            "2" => (EmergencyType.Medical,    "Medical Emergency"),
            "3" => (EmergencyType.Flood,      "Flood / Landslide"),
            "4" => (EmergencyType.Other,      "Other emergency"),
            _   => (EmergencyType.Other,      "Other emergency")
        };

        private static EmergencySeverity MapSeverity(string code) => code switch
        {
            "1" => EmergencySeverity.Critical,
            "2" => EmergencySeverity.High,
            "3" => EmergencySeverity.Medium,
            _   => EmergencySeverity.Low
        };

        private static string MapAffected(string code) => code switch
        {
            "1" => "1",
            "2" => "2-5",
            "3" => "6-20",
            "4" => ">20",
            _   => "unknown"
        };

        private static int GuessPeopleCount(string code) => code switch
        {
            "1" => 1,
            "2" => 3,
            "3" => 10,
            "4" => 25,
            _   => 1
        };

        private static string NormalizePhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return "";
            phone = phone.Trim();
            if (!phone.StartsWith("+")) phone = "+" + phone.TrimStart('0');
            return phone;
        }

        private static string Truncate(string? s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
    }
}
