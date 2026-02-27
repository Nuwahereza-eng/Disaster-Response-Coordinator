using DRC.Api.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace DRC.Api.Services
{
    public class ChatCacheService : IChatCacheService
    {
        private readonly IDistributedCache _cache;
        private readonly ILogger<ChatCacheService> _logger;
        
        public ChatCacheService(IDistributedCache cache, ILogger<ChatCacheService> logger)
        {
            _cache = cache;
            _logger = logger;
        }
        
        public async Task SaveConversationAsync(Guid id, List<ChatMessage> conversation)
        {
            try
            {
                var serialized = JsonSerializer.Serialize(conversation);
                await _cache.SetStringAsync(
                    $"chat:{id}", 
                    serialized,
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                    });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving conversation {Id} to cache", id);
            }
        }

        public async Task<List<ChatMessage>?> GetConversationAsync(Guid id)
        {
            try
            {
                var serialized = await _cache.GetStringAsync($"chat:{id}");
                if (string.IsNullOrEmpty(serialized))
                    return null;
                    
                return JsonSerializer.Deserialize<List<ChatMessage>>(serialized);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting conversation {Id} from cache", id);
                return null;
            }
        }
    }
}
