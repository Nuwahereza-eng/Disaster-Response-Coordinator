namespace DRC.Api.Interfaces
{
    public interface ISmsService
    {
        /// <summary>
        /// Send an SMS message to a phone number
        /// </summary>
        /// <param name="phoneNumber">The recipient phone number (with country code e.g. +256...)</param>
        /// <param name="message">The SMS message content</param>
        /// <returns>True if sent successfully</returns>
        Task<bool> SendSmsAsync(string phoneNumber, string message);
    }
}
