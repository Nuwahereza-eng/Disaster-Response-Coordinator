using DRC.Api.Models;

namespace DRC.Api.Interfaces
{
    /// <summary>
    /// Agent service that performs actions on behalf of the user
    /// </summary>
    public interface IAgentService
    {
        /// <summary>
        /// Process a user message and take appropriate actions
        /// Location coordinates are used for immediate emergency dispatch when critical situations are detected.
        /// </summary>
        Task<AgentResponse> ProcessMessageAsync(Guid? sessionId, string message, string? userPhone = null, int? userId = null, double? latitude = null, double? longitude = null);

        /// <summary>
        /// Get user's active session or sessions history
        /// </summary>
        Task<List<AgentSession>> GetUserSessionsAsync(int userId);

        /// <summary>
        /// Get the current session state including all actions taken
        /// </summary>
        Task<AgentSession?> GetSessionAsync(Guid sessionId);

        /// <summary>
        /// Get all actions taken in a session
        /// </summary>
        Task<List<AgentAction>> GetSessionActionsAsync(Guid sessionId);

        /// <summary>
        /// Get status of a specific action
        /// </summary>
        Task<AgentAction?> GetActionStatusAsync(string actionId);
    }
}
