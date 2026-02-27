namespace DRC.Api.Interfaces
{
    public interface IChatService
    {
        Task<(string? guid, string message)> SendMessage(Guid? guid, string message);
    }
}
