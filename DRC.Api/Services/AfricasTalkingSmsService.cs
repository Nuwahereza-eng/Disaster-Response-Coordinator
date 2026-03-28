using AfricasTalkingCS;
using DRC.Api.Interfaces;

namespace DRC.Api.Services
{
    public class AfricasTalkingSmsService : ISmsService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AfricasTalkingSmsService> _logger;
        private readonly AfricasTalkingGateway _gateway;
        private readonly string _senderId;

        public AfricasTalkingSmsService(IConfiguration configuration, ILogger<AfricasTalkingSmsService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            var username = _configuration["Apps:AfricasTalking:Username"] ?? "sandbox";
            var apiKey = _configuration["Apps:AfricasTalking:ApiKey"] ?? "";
            _senderId = _configuration["Apps:AfricasTalking:SenderId"] ?? "DRC_UG";

            // Determine environment: sandbox username means sandbox environment
            var environment = username.Equals("sandbox", StringComparison.OrdinalIgnoreCase)
                ? "sandbox"
                : "production";

            _gateway = new AfricasTalkingGateway(username, apiKey, environment);
            _logger.LogInformation("📱 Africa's Talking SMS service initialized (env: {Environment}, user: {Username})", environment, username);
        }

        public async Task<bool> SendSmsAsync(string phoneNumber, string message)
        {
            try
            {
                if (string.IsNullOrEmpty(phoneNumber))
                {
                    _logger.LogWarning("SMS send failed: phone number is empty");
                    return false;
                }

                // Format the phone number to international format
                var formattedPhone = FormatPhoneNumber(phoneNumber);

                _logger.LogInformation("📱 Sending SMS to {Phone} (formatted: {FormattedPhone})", phoneNumber, formattedPhone);

                // Send SMS via Africa's Talking - the SDK call is synchronous, wrap in Task.Run
                var result = await Task.Run(() => _gateway.SendMessage(formattedPhone, message));
                string resultStr = result?.ToString() ?? "OK";

                _logger.LogInformation("📱 SMS sent successfully to {Phone}. Response: {Result}", formattedPhone, resultStr);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "📱 Failed to send SMS to {Phone}: {Message}", phoneNumber, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Formats a phone number to international format (+256...)
        /// </summary>
        private string FormatPhoneNumber(string phone)
        {
            if (string.IsNullOrEmpty(phone)) return phone;

            // Remove spaces, dashes, parentheses
            var cleaned = phone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");

            // Already in international format with +
            if (cleaned.StartsWith("+"))
                return cleaned;

            // Uganda local format: starts with 07 -> add +256
            if (cleaned.StartsWith("07") && cleaned.Length == 10)
            {
                return "+256" + cleaned.Substring(1); // 0779081600 -> +256779081600
            }

            // Starts with country code without +
            if (cleaned.StartsWith("256") && cleaned.Length == 12)
            {
                return "+" + cleaned;
            }

            // If starts with 0 and has digits, assume local Uganda number
            if (cleaned.StartsWith("0"))
            {
                return "+256" + cleaned.Substring(1);
            }

            // Default: prepend + if looks like a full number
            if (cleaned.Length >= 10)
            {
                return "+" + cleaned;
            }

            return cleaned;
        }
    }
}
