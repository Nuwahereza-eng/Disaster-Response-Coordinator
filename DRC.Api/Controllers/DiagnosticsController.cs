using DRC.Api.Interfaces;
using DRC.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DRC.Api.Controllers
{
    /// <summary>
    /// Operational diagnostics for the notification pipeline.
    ///
    /// Exists so the demo operator can answer "why didn't my next-of-kin
    /// alert arrive?" in 30 seconds instead of grepping Render logs:
    ///   GET /api/diagnostics/notify-config
    ///       — what's configured (no secrets leaked)
    ///   POST /api/diagnostics/notify-test?phone=+256...&amp;email=...
    ///       — actually fires SMS + WhatsApp + Email and returns explicit results
    /// </summary>
    [ApiController]
    [Route("api/diagnostics")]
    [AllowAnonymous]
    public class DiagnosticsController : ControllerBase
    {
        private readonly IConfiguration _cfg;
        private readonly ISmsService _sms;
        private readonly IEmailService _email;
        private readonly Lazy<IWhatAppService> _whatsApp;
        private readonly ILogger<DiagnosticsController> _logger;

        public DiagnosticsController(
            IConfiguration cfg,
            ISmsService sms,
            IEmailService email,
            Lazy<IWhatAppService> whatsApp,
            ILogger<DiagnosticsController> logger)
        {
            _cfg = cfg;
            _sms = sms;
            _email = email;
            _whatsApp = whatsApp;
            _logger = logger;
        }

        /// <summary>
        /// Reports which credentials are configured WITHOUT leaking values.
        /// Tells you instantly which Render env vars are missing/malformed.
        /// </summary>
        [HttpGet("notify-config")]
        public IActionResult NotifyConfig()
        {
            string Mask(string? v) =>
                string.IsNullOrEmpty(v) ? "" :
                v.Length <= 4 ? "***" : v.Substring(0, 2) + "***" + v.Substring(v.Length - 2);

            var atUser   = _cfg["Apps:AfricasTalking:Username"];
            var atKey    = _cfg["Apps:AfricasTalking:ApiKey"];
            var atSender = _cfg["Apps:AfricasTalking:SenderId"];

            var resendKey = _cfg["Email:ResendApiKey"]
                            ?? Environment.GetEnvironmentVariable("RESEND_API_KEY");
            var fromEmail = _cfg["Email:FromAddress"]
                            ?? Environment.GetEnvironmentVariable("RESEND_FROM_EMAIL");

            var waToken    = _cfg["Apps:Meta:Token"];
            var waPhoneId  = _cfg["Apps:Meta:WhatsAppBusinessPhoneNumberId"];

            // Detect the classic "user pasted KEY=value as the value" mistake
            string? Suspicious(string? v, string keyName) =>
                !string.IsNullOrEmpty(v) && v.Contains("=") && v.StartsWith(keyName, StringComparison.OrdinalIgnoreCase)
                    ? "value contains '=' and starts with key name — likely whole 'KEY=value' line was pasted"
                    : null;

            return Ok(new
            {
                sms = new
                {
                    provider = "AfricasTalking",
                    username = atUser,
                    usernameLooksValid = !string.IsNullOrEmpty(atUser) && !atUser.Contains("="),
                    apiKeyConfigured = !string.IsNullOrEmpty(atKey),
                    apiKeyMasked = Mask(atKey),
                    senderId = atSender,
                    warnings = new[]
                    {
                        Suspicious(atUser, "Apps__AfricasTalking__Username"),
                        Suspicious(atKey, "Apps__AfricasTalking__ApiKey"),
                        string.IsNullOrEmpty(atUser) ? "Apps:AfricasTalking:Username NOT SET — will default to 'sandbox'" : null,
                        string.IsNullOrEmpty(atKey)  ? "Apps:AfricasTalking:ApiKey NOT SET — SMS will fail" : null
                    }.Where(w => w != null)
                },
                email = new
                {
                    provider = string.IsNullOrEmpty(resendKey) ? "SMTP fallback (BLOCKED on Render)" : "Resend HTTP API",
                    resendKeyConfigured = !string.IsNullOrEmpty(resendKey),
                    resendKeyMasked = Mask(resendKey),
                    fromAddress = fromEmail ?? "onboarding@resend.dev",
                    warnings = new[]
                    {
                        Suspicious(resendKey, "RESEND_API_KEY"),
                        string.IsNullOrEmpty(resendKey)
                            ? "RESEND_API_KEY NOT SET — emails will silently fail on Render (SMTP egress is blocked on free tier)"
                            : null,
                        !string.IsNullOrEmpty(resendKey) && !resendKey.StartsWith("re_")
                            ? "RESEND_API_KEY does not start with 're_' — likely invalid"
                            : null
                    }.Where(w => w != null)
                },
                whatsApp = new
                {
                    provider = "Meta WhatsApp Cloud API",
                    tokenConfigured = !string.IsNullOrEmpty(waToken),
                    tokenMasked = Mask(waToken),
                    phoneNumberId = waPhoneId,
                    warnings = new[]
                    {
                        string.IsNullOrEmpty(waToken)    ? "Apps:Meta:Token NOT SET" : null,
                        string.IsNullOrEmpty(waPhoneId)  ? "Apps:Meta:WhatsAppBusinessPhoneNumberId NOT SET" : null,
                        "WhatsApp recipient must have messaged your business number first (Meta policy)"
                    }.Where(w => w != null)
                },
                hint = "POST /api/diagnostics/notify-test?phone=+256...&email=you@example.com to actually fire test messages."
            });
        }

        /// <summary>
        /// Actually fires a test SMS, WhatsApp message, and email and returns
        /// the success/failure of each channel with the underlying error.
        /// Use this from the Render shell or your phone:
        ///   curl -X POST "https://your-api.onrender.com/api/diagnostics/notify-test?phone=+256779081600&email=you@example.com"
        /// </summary>
        [HttpPost("notify-test")]
        public async Task<IActionResult> NotifyTest(
            [FromQuery] string? phone,
            [FromQuery] string? email)
        {
            var results = new
            {
                smsResult   = (object?)null,
                waResult    = (object?)null,
                emailResult = (object?)null
            };

            object? smsResult = null;
            object? waResult = null;
            object? emailResult = null;

            const string testBody =
                "DiReCo diagnostic test — if you got this, the notification pipeline works. " +
                "Reply STOP to opt out.";

            // ---- SMS ----
            if (!string.IsNullOrWhiteSpace(phone))
            {
                try
                {
                    var ok = await _sms.SendSmsAsync(phone, testBody);
                    smsResult = new { ok, channel = "AfricasTalking SMS", to = phone };
                }
                catch (Exception ex)
                {
                    smsResult = new { ok = false, channel = "AfricasTalking SMS", to = phone, error = ex.Message };
                }
            }
            else
            {
                smsResult = new { skipped = "no phone query param" };
            }

            // ---- WhatsApp ----
            if (!string.IsNullOrWhiteSpace(phone))
            {
                try
                {
                    var waPhone = phone.Replace(" ", "").Replace("-", "").TrimStart('+');
                    if (waPhone.StartsWith("0") && waPhone.Length == 10) waPhone = "256" + waPhone.Substring(1);
                    await _whatsApp.Value.SendMessage(waPhone, "🚨 DiReCo diagnostic test (WhatsApp).");
                    waResult = new { ok = true, channel = "Meta WhatsApp Cloud", to = waPhone };
                }
                catch (Exception ex)
                {
                    waResult = new
                    {
                        ok = false,
                        channel = "Meta WhatsApp Cloud",
                        error = ex.Message,
                        hint = "Recipient must have messaged your business in the last 24h, OR you must use an approved template."
                    };
                }
            }
            else
            {
                waResult = new { skipped = "no phone query param" };
            }

            // ---- Email ----
            if (!string.IsNullOrWhiteSpace(email))
            {
                try
                {
                    var ok = await _email.SendEmergencyAlertAsync(
                        email,
                        "Diagnostic Recipient",
                        "DiReCo Test",
                        "Test",
                        "Low",
                        "Diagnostic location",
                        testBody);
                    emailResult = new { ok, channel = "Resend or SMTP", to = email };
                }
                catch (Exception ex)
                {
                    emailResult = new { ok = false, channel = "Resend or SMTP", to = email, error = ex.Message };
                }
            }
            else
            {
                emailResult = new { skipped = "no email query param" };
            }

            return Ok(new
            {
                sms = smsResult,
                whatsApp = waResult,
                email = emailResult,
                checkedAt = DateTime.UtcNow
            });
        }
    }
}
