using DRC.Api.Services;

namespace DRC.Api.Interfaces
{
    public interface IChatCacheService
    {
        Task SaveConversationAsync(Guid id, List<ChatMessage> conversation);
        Task<List<ChatMessage>?> GetConversationAsync(Guid id);
    }
}
