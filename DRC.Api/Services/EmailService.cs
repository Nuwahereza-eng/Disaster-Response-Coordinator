using DRC.Api.Interfaces;
using System.Net;
using System.Net.Mail;

namespace DRC.Api.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;
        private readonly string _smtpHost;
        private readonly int _smtpPort;
        private readonly string _smtpUsername;
        private readonly string _smtpPassword;
        private readonly string _fromEmail;
        private readonly string _fromName;
        private readonly bool _isConfigured;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            
            // Load SMTP settings from configuration
            _smtpHost = _configuration["Email:SmtpHost"] ?? Environment.GetEnvironmentVariable("EMAIL_SMTP_HOST") ?? "";
            _smtpPort = int.TryParse(_configuration["Email:SmtpPort"] ?? Environment.GetEnvironmentVariable("EMAIL_SMTP_PORT"), out var port) ? port : 587;
            _smtpUsername = _configuration["Email:Username"] ?? Environment.GetEnvironmentVariable("EMAIL_USERNAME") ?? "";
            _smtpPassword = _configuration["Email:Password"] ?? Environment.GetEnvironmentVariable("EMAIL_PASSWORD") ?? "";
            _fromEmail = _configuration["Email:FromEmail"] ?? Environment.GetEnvironmentVariable("EMAIL_FROM") ?? "alerts@drc.ug";
            _fromName = _configuration["Email:FromName"] ?? "Uganda Disaster Response";
            
            _isConfigured = !string.IsNullOrEmpty(_smtpHost) && !string.IsNullOrEmpty(_smtpUsername) && !string.IsNullOrEmpty(_smtpPassword);
            
            if (!_isConfigured)
            {
                _logger.LogWarning("📧 Email service not configured. Set EMAIL_SMTP_HOST, EMAIL_USERNAME, EMAIL_PASSWORD environment variables.");
            }
            else
            {
                _logger.LogInformation("📧 Email service configured with SMTP host: {Host}", _smtpHost);
            }
        }

        public async Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, string? textBody = null)
        {
            if (!_isConfigured)
            {
                _logger.LogWarning("📧 Email not sent - service not configured. Would send to: {Email}", toEmail);
                return false;
            }

            if (string.IsNullOrEmpty(toEmail))
            {
                _logger.LogWarning("📧 Email not sent - no email address provided");
                return false;
            }

            try
            {
                using var smtpClient = new SmtpClient(_smtpHost, _smtpPort)
                {
                    Credentials = new NetworkCredential(_smtpUsername, _smtpPassword),
                    EnableSsl = true,
                    DeliveryMethod = SmtpDeliveryMethod.Network
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
                
                _logger.LogInformation("📧 Email sent successfully to {Email}", toEmail);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "📧 Failed to send email to {Email}", toEmail);
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
