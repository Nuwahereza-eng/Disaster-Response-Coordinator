namespace DRC.Api.Interfaces
{
    public interface IEmailService
    {
        Task<bool> SendEmailAsync(string toEmail, string toName, string subject, string htmlBody, string? textBody = null);
        Task<bool> SendEmergencyAlertAsync(string toEmail, string toName, string userName, string emergencyType, string severity, string location, string situation);
    }
}
