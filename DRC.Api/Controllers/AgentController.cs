using DRC.Api.Interfaces;
using DRC.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DRC.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AgentController : ControllerBase
    {
        private readonly IAgentService _agentService;
        private readonly ILogger<AgentController> _logger;

        public AgentController(IAgentService agentService, ILogger<AgentController> logger)
        {
            _agentService = agentService;
            _logger = logger;
        }

        /// <summary>
        /// Send a message to the agent. The agent will understand your request and take actions on your behalf.
        /// Optionally accepts JWT authentication to link session to user account.
        /// Location coordinates are used for immediate emergency dispatch.
        /// </summary>
        [HttpPost("Conversation")]
        public async Task<IActionResult> Conversation(
            [FromBody] string message, 
            [FromQuery] Guid? guid = null, 
            [FromQuery] string? phone = null,
            [FromQuery] double? latitude = null,
            [FromQuery] double? longitude = null)
        {
            // Try to extract user ID from JWT if authenticated
            int? userId = null;
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                           ?? User.FindFirst("sub")?.Value
                           ?? User.FindFirst("userId")?.Value;
            
            if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out var parsedUserId))
            {
                userId = parsedUserId;
            }

            // Also try to get phone from claims if not provided
            if (string.IsNullOrEmpty(phone))
            {
                phone = User.FindFirst(ClaimTypes.MobilePhone)?.Value 
                     ?? User.FindFirst("phone")?.Value;
            }

            _logger.LogInformation("Agent request received - SessionId: {SessionId}, UserId: {UserId}, Location: ({Lat}, {Lng}), Phone: {Phone}, Message length: {Length}", 
                guid, userId, latitude, longitude, phone != null ? "[provided]" : "[none]", message?.Length ?? 0);
            
            var response = await _agentService.ProcessMessageAsync(guid, message, phone, userId, latitude, longitude);
            
            return Ok(new 
            { 
                Guid = response.SessionId, 
                Response = response.Message,
                ActionsTaken = response.ActionsTaken.Select(a => new 
                {
                    a.Id,
                    a.ToolName,
                    a.Description,
                    Status = a.Status.ToString(),
                    a.CompletedAt
                }),
                IsEmergency = response.IsEmergency,
                Severity = response.Severity?.ToString()
            });
        }

        /// <summary>
        /// Get the current user's agent sessions (requires authentication)
        /// </summary>
        [Authorize]
        [HttpGet("MySessions")]
        public async Task<IActionResult> GetMySessions()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                           ?? User.FindFirst("sub")?.Value
                           ?? User.FindFirst("userId")?.Value;
            
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
            {
                return Unauthorized(new { Error = "User ID not found in token" });
            }

            var sessions = await _agentService.GetUserSessionsAsync(userId);
            
            return Ok(new
            {
                UserId = userId,
                TotalSessions = sessions.Count,
                Sessions = sessions.Select(s => new
                {
                    s.SessionId,
                    s.UserLocation,
                    s.CreatedAt,
                    s.LastActivityAt,
                    MessageCount = s.Messages.Count,
                    ActionsCount = s.ActionsTaken.Count
                })
            });
        }

        /// <summary>
        /// Get the current session state including all messages and actions taken
        /// </summary>
        [HttpGet("Session/{sessionId}")]
        public async Task<IActionResult> GetSession(Guid sessionId)
        {
            var session = await _agentService.GetSessionAsync(sessionId);
            
            if (session == null)
                return NotFound(new { Error = "Session not found" });
            
            return Ok(new
            {
                session.SessionId,
                session.UserPhone,
                session.UserName,
                session.UserLocation,
                session.CreatedAt,
                session.LastActivityAt,
                MessageCount = session.Messages.Count,
                ActionsCount = session.ActionsTaken.Count,
                ActiveAlerts = session.ActiveAlertIds,
                RecentActions = session.ActionsTaken.TakeLast(5).Select(a => new
                {
                    a.Id,
                    a.ToolName,
                    a.Description,
                    Status = a.Status.ToString(),
                    a.CreatedAt,
                    a.CompletedAt
                })
            });
        }

        /// <summary>
        /// Get all actions taken in a session
        /// </summary>
        [HttpGet("Session/{sessionId}/Actions")]
        public async Task<IActionResult> GetSessionActions(Guid sessionId)
        {
            var actions = await _agentService.GetSessionActionsAsync(sessionId);
            
            return Ok(new
            {
                SessionId = sessionId,
                TotalActions = actions.Count,
                Actions = actions.Select(a => new
                {
                    a.Id,
                    a.ToolName,
                    a.Description,
                    a.Parameters,
                    Status = a.Status.ToString(),
                    a.Result,
                    a.CreatedAt,
                    a.CompletedAt
                })
            });
        }

        /// <summary>
        /// Get status of a specific action
        /// </summary>
        [HttpGet("Action/{actionId}")]
        public async Task<IActionResult> GetActionStatus(string actionId)
        {
            var action = await _agentService.GetActionStatusAsync(actionId);
            
            if (action == null)
                return NotFound(new { Error = "Action not found" });
            
            return Ok(new
            {
                action.Id,
                action.ToolName,
                action.Description,
                action.Parameters,
                Status = action.Status.ToString(),
                action.Result,
                action.CreatedAt,
                action.CompletedAt
            });
        }
    }
}
