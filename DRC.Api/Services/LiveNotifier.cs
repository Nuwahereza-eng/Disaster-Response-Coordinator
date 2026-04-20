using DRC.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace DRC.Api.Services
{
    /// <summary>
    /// Thin helper around <see cref="LiveHub"/> so controllers and services
    /// can push real-time events without depending on SignalR types directly.
    /// </summary>
    public interface ILiveNotifier
    {
        Task EmergencyCreatedAsync(object payload);
        Task ActionDispatchedAsync(string sessionId, object payload);
        Task AlertBroadcastAsync(object payload);
    }

    public class LiveNotifier : ILiveNotifier
    {
        private readonly IHubContext<LiveHub> _hub;
        private readonly ILogger<LiveNotifier> _logger;

        public LiveNotifier(IHubContext<LiveHub> hub, ILogger<LiveNotifier> logger)
        {
            _hub = hub;
            _logger = logger;
        }

        public async Task EmergencyCreatedAsync(object payload)
        {
            try
            {
                await _hub.Clients.Group("admins").SendAsync("emergencyCreated", payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR emergencyCreated broadcast failed");
            }
        }

        public async Task ActionDispatchedAsync(string sessionId, object payload)
        {
            try
            {
                await Task.WhenAll(
                    _hub.Clients.Group("admins").SendAsync("actionDispatched", payload),
                    _hub.Clients.Group($"session:{sessionId}").SendAsync("actionDispatched", payload)
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR actionDispatched broadcast failed");
            }
        }

        public async Task AlertBroadcastAsync(object payload)
        {
            try
            {
                await _hub.Clients.All.SendAsync("alertBroadcast", payload);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR alertBroadcast failed");
            }
        }
    }
}
