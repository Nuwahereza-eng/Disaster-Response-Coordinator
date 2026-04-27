using System.Text.RegularExpressions;
using DRC.Api.Data;
using DRC.Api.Data.Entities;
using DRC.Api.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DRC.Api.Controllers
{
    /// <summary>
    /// Africa's Talking inbound-SMS callback.
    ///
    /// Configure the AT dashboard short-code/long-number callback to:
    ///     POST {API_BASE}/api/sms/incoming
    ///
    /// Designed for OFFLINE-CAPABLE reporting: a citizen with no data plan
    /// just texts a few words ("FIRE Bududa church") and gets the same
    /// AI-driven dispatch + Next-of-Kin alerts as the web/WhatsApp users.
    ///
    /// Two parsing modes (in order):
    ///   1. Keyword shortcut  — "FIRE &lt;location&gt;", "MED &lt;loc&gt;", "FLOOD &lt;loc&gt;",
    ///      "LAND &lt;loc&gt;", "SOS &lt;loc&gt;". Creates EmergencyRequest directly so
    ///      it works even if the LLM is down/rate-limited.
    ///   2. Free text         — anything else is forwarded to the AI agent,
    ///      which classifies it, dispatches services, and notifies Next of Kin.
    /// </summary>
    [ApiController]
    [Route("api/sms")]
    public class InboundSmsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IAgentService _agent;
        private readonly ISmsService _sms;
        private readonly DRC.Api.Services.ILiveNotifier _live;
        private readonly ILogger<InboundSmsController> _logger;

        public InboundSmsController(
            ApplicationDbContext db,
            IAgentService agent,
            ISmsService sms,
            DRC.Api.Services.ILiveNotifier live,
            ILogger<InboundSmsController> logger)
        {
            _db = db;
            _agent = agent;
            _sms = sms;
            _live = live;
            _logger = logger;
        }

        /// <summary>
        /// Health/help endpoint — texting "HELP" to the short-code routes here too,
        /// but if a developer hits this URL in a browser they get the keyword cheat-sheet.
        /// </summary>
        [HttpGet("incoming")]
        [AllowAnonymous]
        public IActionResult Help() => Ok(new
        {
            usage = "POST form-encoded { from, to, text, date, id, linkId } from Africa's Talking",
            keywords = new[]
            {
                "FIRE <location>     — report a fire",
                "MED  <location>     — medical emergency",
                "FLOOD <location>    — flooding",
                "LAND <location>     — landslide",
                "SOS  <location>     — generic critical SOS",
                "HELP                — get this menu via SMS"
            }
        });

        /// <summary>
        /// Africa's Talking POSTs here every time the short-code receives an SMS.
        /// Form fields: from, to, text, date, id, linkId.
        /// </summary>
        [HttpPost("incoming")]
        [AllowAnonymous]
        [Consumes("application/x-www-form-urlencoded", "application/json", "text/plain")]
        public async Task<IActionResult> Incoming(
            [FromForm] string? from,
            [FromForm] string? to,
            [FromForm] string? text,
            [FromForm] string? date,
            [FromForm] string? id,
            [FromForm] string? linkId)
        {
            from = (from ?? "").Trim();
            text = (text ?? "").Trim();

            _logger.LogInformation("📩 Inbound SMS id={Id} from={From} to={To} text='{Text}'",
                id, from, to, text);

            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(text))
            {
                return Ok(new { status = "ignored", reason = "empty" });
            }

            // -------- HELP / menu --------
            if (text.Equals("HELP", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("MENU", StringComparison.OrdinalIgnoreCase) ||
                text.Equals("?", StringComparison.OrdinalIgnoreCase))
            {
                _ = ReplyAsync(from,
                    "DIRECO emergency reporting:\n" +
                    "FIRE <place>\n" +
                    "MED <place>\n" +
                    "FLOOD <place>\n" +
                    "LAND <place>\n" +
                    "SOS <place>\n" +
                    "Or just describe what's happening.\n" +
                    "Dial *384*10000# for menu.");
                return Ok(new { status = "ok", route = "help" });
            }

            // -------- 1. Keyword shortcut (works without AI/data) --------
            var quick = TryParseKeyword(text);
            if (quick != null)
            {
                var (type, sev, label, location) = quick.Value;

                var req = new EmergencyRequest
                {
                    Type = type,
                    Severity = sev,
                    Status = RequestStatus.Pending,
                    Description = $"[SMS] {label} reported. Location: {location}. Original text: \"{text}\"",
                    Location = location,
                    UserPhone = NormalizePhone(from),
                    CreatedAt = DateTime.UtcNow
                };

                _db.EmergencyRequests.Add(req);
                await _db.SaveChangesAsync();

                // Live push to admin dashboard
                try
                {
                    await _live.EmergencyCreatedAsync(new
                    {
                        id = req.Id,
                        type = req.Type.ToString(),
                        severity = req.Severity.ToString(),
                        description = req.Description,
                        location = req.Location,
                        phone = req.UserPhone,
                        channel = "SMS",
                        createdAt = req.CreatedAt
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SignalR push failed for SMS emergency #{Id}", req.Id);
                }

                _logger.LogInformation("🚨 SMS emergency #{Id} ({Type}/{Sev}) from {Phone}",
                    req.Id, type, sev, from);

                // Confirm to sender + escalate via agent (so NoK gets pinged + AI replies)
                _ = Task.Run(async () =>
                {
                    var ack = $"DIRECO: {label} report #{req.Id} received. Severity: {sev}. " +
                              $"Help is being coordinated. If life is at risk dial 999 now.";
                    try { await _sms.SendSmsAsync(from, ack); }
                    catch (Exception ex) { _logger.LogWarning(ex, "ACK SMS failed"); }

                    // Also kick the agent so Next-of-Kin gets notified (reuse existing pipeline).
                    try
                    {
                        var agentMsg = $"{label} emergency at {location}. Reported by SMS keyword shortcut.";
                        await _agent.ProcessMessageAsync(null, agentMsg, NormalizePhone(from));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Agent escalation after SMS keyword failed");
                    }
                });

                return Ok(new { status = "ok", route = "keyword", emergencyId = req.Id });
            }

            // -------- 2. Free-text → AI agent (handles classification + dispatch + NoK) --------
            _ = Task.Run(async () =>
            {
                try
                {
                    var resp = await _agent.ProcessMessageAsync(null, text, NormalizePhone(from));
                    var reply = string.IsNullOrWhiteSpace(resp?.Message)
                        ? "DIRECO: Your message was received. A coordinator will follow up."
                        : Truncate("DIRECO: " + resp!.Message, 320);
                    await _sms.SendSmsAsync(from, reply);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Agent processing failed for inbound SMS from {From}", from);
                    try
                    {
                        await _sms.SendSmsAsync(from,
                            "DIRECO: We received your message but our system is busy. " +
                            "If life is at risk dial 999, or text FIRE/MED/FLOOD/LAND <place>.");
                    }
                    catch { /* best-effort */ }
                }
            });

            return Ok(new { status = "ok", route = "agent" });
        }

        // ---------------------------------------------------------------
        // Keyword parser
        // ---------------------------------------------------------------
        private static (EmergencyType, EmergencySeverity, string label, string location)?
            TryParseKeyword(string text)
        {
            var m = Regex.Match(text,
                @"^(?<kw>FIRE|MED(?:ICAL)?|FLOOD|LAND(?:SLIDE)?|SOS|EMERG(?:ENCY)?)\b\s*(?<loc>.*)$",
                RegexOptions.IgnoreCase);

            if (!m.Success) return null;

            var kw = m.Groups["kw"].Value.ToUpperInvariant();
            var loc = m.Groups["loc"].Value.Trim();
            if (string.IsNullOrWhiteSpace(loc)) loc = "Location not specified";

            return kw switch
            {
                "FIRE"                          => (EmergencyType.Fire,       EmergencySeverity.High,     "Fire",      loc),
                "MED" or "MEDICAL"              => (EmergencyType.Medical,    EmergencySeverity.High,     "Medical",   loc),
                "FLOOD"                         => (EmergencyType.Flood,      EmergencySeverity.High,     "Flood",     loc),
                "LAND" or "LANDSLIDE"           => (EmergencyType.Earthquake, EmergencySeverity.High,     "Landslide", loc),
                "SOS" or "EMERG" or "EMERGENCY" => (EmergencyType.Other,      EmergencySeverity.Critical, "SOS",       loc),
                _                               => ((EmergencyType, EmergencySeverity, string, string)?)null
            };
        }

        private async Task ReplyAsync(string to, string body)
        {
            try { await _sms.SendSmsAsync(to, body); }
            catch (Exception ex) { _logger.LogWarning(ex, "Reply SMS to {To} failed", to); }
        }

        private static string NormalizePhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return "";
            phone = phone.Trim();
            if (!phone.StartsWith("+")) phone = "+" + phone.TrimStart('0');
            return phone;
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..(max - 1)] + "…");
    }
}
