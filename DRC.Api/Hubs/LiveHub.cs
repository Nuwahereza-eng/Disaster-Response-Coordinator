using Microsoft.AspNetCore.SignalR;

namespace DRC.Api.Hubs
{
    /// <summary>
    /// Real-time push channel used by the admin dashboard and the chat UI.
    ///
    /// Server → client events:
    ///   "emergencyCreated"   payload: { id, type, severity, description, location, channel, createdAt }
    ///   "actionDispatched"   payload: { sessionId, toolName, description, status, timestamp }
    ///   "alertBroadcast"     payload: { title, message, severity, area, createdAt }
    ///
    /// Groups:
    ///   "admins"  — the admin dashboard joins this group
    ///   "session:{guid}" — a chat session joins its own group
    /// </summary>
    public class LiveHub : Hub
    {
        public Task JoinAdmins() =>
            Groups.AddToGroupAsync(Context.ConnectionId, "admins");

        public Task LeaveAdmins() =>
            Groups.RemoveFromGroupAsync(Context.ConnectionId, "admins");

        public Task JoinSession(string sessionId) =>
            Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");

        public Task LeaveSession(string sessionId) =>
            Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session:{sessionId}");
    }
}
