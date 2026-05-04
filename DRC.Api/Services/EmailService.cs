using DRC.Api.Interfaces;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;

namespace DRC.Api.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;
        private readonly IHttpClientFactory _httpFactory;

        // Resend (HTTP API — works on Render where SMTP egress is blocked)
        private readonly string _resendApiKey;
        private readonly bool _useResend;

        // SMTP fallback (used for local dev when Resend isn't configured)
        private readonly string _smtpHost;
        private readonly int _smtpPort;
        private readonly string _smtpUsername;
        private readonly string _smtpPassword;
        private readonly bool _smtpConfigured;

        private readonly string _fromEmail;
        private readonly string _fromName;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger, IHttpClientFactory httpFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _httpFactory = httpFactory;

            // ---- Resend (preferred on Render / production) ----
            _resendApiKey = _configuration["Email:ResendApiKey"]
                ?? Environment.GetEnvironmentVariable("RESEND_API_KEY") ?? "";
            _useResend = !string.IsNullOrWhiteSpace(_resendApiKey);

            // ---- SMTP fallback (local dev) ----
            _smtpHost = _configuration["Email:SmtpHost"] ?? Environment.GetEnvironmentVariable("EMAIL_SMTP_HOST") ?? "";
            _smtpPort = int.TryParse(_configuration["Email:SmtpPort"] ?? Environment.GetEnvironmentVariable("EMAIL_SMTP_PORT"), out var port) ? port : 587;
            _smtpUsername = _configuration["Email:Username"] ?? Environment.GetEnvironmentVariable("EMAIL_USERNAME") ?? "";
            _smtpPassword = _configuration["Email:Password"] ?? Environment.GetEnvironmentVariable("EMAIL_PASSWORD") ?? "";
            _smtpConfigured = !string.IsNullOrEmpty(_smtpHost) && !string.IsNullOrEmpty(_smtpUsername) && !string.IsNullOrEmpty(_smtpPassword);

            _fromEmail = _configuration["Email:FromEmail"]
                ?? Environment.GetEnvironmentVariable("EMAIL_FROM")
                ?? "onboarding@resend.dev";
            _fromName = _configuration["Email:FromName"]
                ?? Environment.GetEnvironmentVariable("EMAIL_FROM_NAME")
                ?? "Uganda Disaster Response";

            if (_useResend)
                _logger.LogInformation("📧 Email service: Resend HTTP API (from {From})", _fromEmail);
            else if (_smtpConfigured)
                _logger.LogInformation("📧 Email service: SMTP fallback ({Host}:{Port})", _smtpHost, _smtpPort);
            else
                _logger.LogWarning("📧 Email service NOT configured. Set RESEND_API_KEY (preferred) or EMAIL_SMTP_HOST/USERNAME/PASSWORD.");
        }

        public async Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, string? textBody = null)
        {
            if (string.IsNullOrEmpty(toEmail))
            {
                _logger.LogWarning("📧 Email not sent - no recipient address");
                return false;
            }

            if (_useResend)
            {
                return await SendViaResendAsync(toEmail, toName, subject, htmlBody, textBody);
            }
            if (_smtpConfigured)
            {
                return await SendViaSmtpAsync(toEmail, toName, subject, htmlBody);
            }

            _logger.LogWarning("📧 Email not sent - no provider configured. Would send to: {Email}", toEmail);
            return false;
        }

        private async Task<bool> SendViaResendAsync(string toEmail, string toName, string subject, string htmlBody, string? textBody)
        {
            try
            {
                var http = _httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(8);
                http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _resendApiKey);

                var fromHeader = string.IsNullOrWhiteSpace(_fromName) ? _fromEmail : $"{_fromName} <{_fromEmail}>";
                var payload = new
                {
                    from = fromHeader,
                    to = new[] { toEmail },
                    subject,
                    html = htmlBody,
                    text = textBody
                };

                using var resp = await http.PostAsJsonAsync("https://api.resend.com/emails", payload);
                if (resp.IsSuccessStatusCode)
                {
                    _logger.LogInformation("📧 Email sent via Resend to {Email}", toEmail);
                    return true;
                }
                var body = await resp.Content.ReadAsStringAsync();
                _logger.LogError("📧 Resend rejected email to {Email}: {Status} {Body}", toEmail, (int)resp.StatusCode, body);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "📧 Resend send failed for {Email}", toEmail);
                return false;
            }
        }

        private async Task<bool> SendViaSmtpAsync(string toEmail, string toName, string subject, string htmlBody)
        {
            try
            {
                using var smtpClient = new SmtpClient(_smtpHost, _smtpPort)
                {
                    Credentials = new NetworkCredential(_smtpUsername, _smtpPassword),
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 8000
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_fromEmail, _fromName),
                    Subject = subject,
                    IsBodyHtml = true,
                    Body = htmlBody
                };
                mailMessage.To.Add(new MailAddress(toEmail, toName));

                await smtpClient.SendMailAsync(mailMessage);
                _logger.LogInformation("📧 Email sent via SMTP to {Email}", toEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "📧 SMTP send failed for {Email}", toEmail);
                return false;
            }
        }

        public async Task<bool> SendEmergencyAlertAsync(
            string toEmail, 
            string toName, 
            string userName, 
            string emergencyType, 
            string severity, 
            string location, 
            string situation)
        {
            var subject = $"🚨 EMERGENCY ALERT: {userName} needs help!";
            
            var htmlBody = $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background: linear-gradient(135deg, #dc2626, #991b1b); color: white; padding: 20px; border-radius: 10px 10px 0 0; text-align: center; }}
        .header h1 {{ margin: 0; font-size: 24px; }}
        .content {{ background: #fff; border: 1px solid #e5e5e5; padding: 25px; border-radius: 0 0 10px 10px; }}
        .alert-box {{ background: #fef2f2; border-left: 4px solid #dc2626; padding: 15px; margin: 15px 0; }}
        .info-row {{ display: flex; margin: 10px 0; }}
        .label {{ font-weight: bold; width: 120px; color: #666; }}
        .value {{ flex: 1; }}
        .severity-high {{ color: #dc2626; font-weight: bold; }}
        .severity-medium {{ color: #ea580c; font-weight: bold; }}
        .severity-low {{ color: #ca8a04; font-weight: bold; }}
        .footer {{ text-align: center; margin-top: 20px; color: #666; font-size: 12px; }}
        .cta {{ display: inline-block; background: #2563eb; color: white; padding: 12px 24px; text-decoration: none; border-radius: 5px; margin-top: 15px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🚨 EMERGENCY ALERT</h1>
        </div>
        <div class='content'>
            <p>Hello <strong>{toName}</strong>,</p>
            
            <div class='alert-box'>
                <p><strong>{userName}</strong> has reported an emergency and you are listed as their emergency contact.</p>
            </div>
            
            <div class='info-row'>
                <span class='label'>Emergency Type:</span>
                <span class='value'>{emergencyType}</span>
            </div>
            
            <div class='info-row'>
                <span class='label'>Severity:</span>
                <span class='value severity-{severity.ToLower()}'>{severity.ToUpper()}</span>
            </div>
            
            <div class='info-row'>
                <span class='label'>Location:</span>
                <span class='value'>{location}</span>
            </div>
            
            <div class='info-row'>
                <span class='label'>Situation:</span>
                <span class='value'>{situation}</span>
            </div>
            
            <p style='margin-top: 20px;'><strong>What to do:</strong></p>
            <ul>
                <li>Try to contact {userName} immediately</li>
                <li>If you cannot reach them, contact local emergency services</li>
                <li>Share this information with other family members if needed</li>
            </ul>
            
            <p style='margin-top: 15px; padding: 10px; background: #f0fdf4; border-radius: 5px;'>
                ✅ <strong>{userName}</strong> is being assisted by the Uganda Disaster Response system. 
                Emergency services have been notified.
            </p>
        </div>
        <div class='footer'>
            <p>This is an automated message from Uganda Disaster Response Coordinator</p>
            <p>Time sent: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>
        </div>
    </div>
</body>
</html>";

            return await SendEmailAsync(toEmail, toName, subject, htmlBody);
        }
    }
}
