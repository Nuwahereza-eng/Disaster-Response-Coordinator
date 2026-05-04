using System.Text;
using System.Text.Json;

namespace DRC.App.Services
{
    public class AgentClientService
    {
        private readonly HttpClient _httpClient;
        private string? _authToken;

        public AgentClientService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public void SetAuthToken(string? token)
        {
            _authToken = token;
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
            }
        }

        public bool IsAuthenticated => !string.IsNullOrEmpty(_authToken);

        public async Task<AgentConversationResponse> Conversation(string message, Guid? guid = null, double? latitude = null, double? longitude = null, string? phone = null)
        {
            // Build query string with location and phone for emergency response
            var queryParams = new List<string>();
            if (guid.HasValue) queryParams.Add($"guid={guid}");
            if (latitude.HasValue) queryParams.Add($"latitude={latitude.Value}");
            if (longitude.HasValue) queryParams.Add($"longitude={longitude.Value}");
            if (!string.IsNullOrEmpty(phone)) queryParams.Add($"phone={Uri.EscapeDataString(phone)}");
            
            var requestUri = "api/Agent/Conversation" + (queryParams.Any() ? $"?{string.Join("&", queryParams)}" : "");
            var content = new StringContent(JsonSerializer.Serialize(message), Encoding.UTF8, "application/json");

            // Per-call hard cap so the chat spinner can never hang indefinitely,
            // even if the underlying HttpClient timeout misbehaves.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

            using (var response = await _httpClient.PostAsync(requestUri, content, cts.Token))
            {
                response.EnsureSuccessStatusCode();
                var responseContent = await response.Content.ReadAsStringAsync();

                using (JsonDocument doc = JsonDocument.Parse(responseContent))
                {
                    var root = doc.RootElement;
                    var result = new AgentConversationResponse();

                    // Parse GUID
                    if (root.TryGetProperty("guid", out var g1) && g1.ValueKind == JsonValueKind.String)
                        result.Guid = g1.GetGuid();
                    else if (root.TryGetProperty("Guid", out var g2) && g2.ValueKind == JsonValueKind.String)
                        result.Guid = g2.GetGuid();

                    // Parse response message
                    if (root.TryGetProperty("response", out var r1) && r1.ValueKind == JsonValueKind.String)
                        result.Response = r1.GetString() ?? "";
                    else if (root.TryGetProperty("Response", out var r2) && r2.ValueKind == JsonValueKind.String)
                        result.Response = r2.GetString() ?? "";

                    // Parse actions taken
                    if (root.TryGetProperty("actionsTaken", out var actions) && actions.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var action in actions.EnumerateArray())
                        {
                            var agentAction = new AgentActionInfo
                            {
                                Id = action.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                                ToolName = action.TryGetProperty("toolName", out var tn) ? tn.GetString() ?? "" : "",
                                Description = action.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                                Status = action.TryGetProperty("status", out var st) ? st.GetString() ?? "" : ""
                            };
                            result.ActionsTaken.Add(agentAction);
                        }
                    }
                    else if (root.TryGetProperty("ActionsTaken", out var actionsP) && actionsP.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var action in actionsP.EnumerateArray())
                        {
                            var agentAction = new AgentActionInfo
                            {
                                Id = action.TryGetProperty("Id", out var id) ? id.GetString() ?? "" : "",
                                ToolName = action.TryGetProperty("ToolName", out var tn) ? tn.GetString() ?? "" : "",
                                Description = action.TryGetProperty("Description", out var desc) ? desc.GetString() ?? "" : "",
                                Status = action.TryGetProperty("Status", out var st) ? st.GetString() ?? "" : ""
                            };
                            result.ActionsTaken.Add(agentAction);
                        }
                    }

                    // Parse emergency info
                    if (root.TryGetProperty("isEmergency", out var isEmergency))
                        result.IsEmergency = isEmergency.GetBoolean();
                    else if (root.TryGetProperty("IsEmergency", out var isEmergencyP))
                        result.IsEmergency = isEmergencyP.GetBoolean();

                    if (root.TryGetProperty("severity", out var severity) && severity.ValueKind == JsonValueKind.String)
                        result.Severity = severity.GetString();
                    else if (root.TryGetProperty("Severity", out var severityP) && severityP.ValueKind == JsonValueKind.String)
                        result.Severity = severityP.GetString();

                    return result;
                }
            }
        }

        public async Task<ChatHistoryResponse?> GetChatHistoryAsync()
        {
            if (!IsAuthenticated) return null;

            // Hard 10s cap. The shared HttpClient is configured with a 5 min timeout
            // for slow LLM responses, but a chat-history fetch must NEVER hang the
            // sidebar spinner that long — if the DB is down the user wants to know
            // immediately, not in 5 minutes.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                var response = await _httpClient.GetAsync("api/Agent/ChatHistory", cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"ChatHistory returned {(int)response.StatusCode} {response.StatusCode}");
                }

                var content = await response.Content.ReadAsStringAsync(cts.Token);
                return JsonSerializer.Deserialize<ChatHistoryResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                throw new TimeoutException("Chat history request timed out after 10s.");
            }
        }

        public async Task<SessionDetailResponse?> GetSessionAsync(Guid sessionId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"api/Agent/Session/{sessionId}");
                if (!response.IsSuccessStatusCode) return null;
                
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<SessionDetailResponse>(content, new JsonSerializerOptions 
                { 
                    PropertyNameCaseInsensitive = true 
                });
            }
            catch
            {
                return null;
            }
        }
    }

    public class AgentConversationResponse
    {
        public Guid Guid { get; set; }
        public string Response { get; set; } = "";
        public List<AgentActionInfo> ActionsTaken { get; set; } = new();
        public bool IsEmergency { get; set; }
        public string? Severity { get; set; }
    }

    public class AgentActionInfo
    {
        public string Id { get; set; } = "";
        public string ToolName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "";
    }

    public class ChatHistoryResponse
    {
        public int UserId { get; set; }
        public int TotalMessages { get; set; }
        public int TotalSessions { get; set; }
        public List<ChatSessionSummary> Sessions { get; set; } = new();
    }

    public class ChatSessionSummary
    {
        public Guid SessionId { get; set; }
        public DateTime FirstMessageAt { get; set; }
        public DateTime LastMessageAt { get; set; }
        public int MessageCount { get; set; }
        public List<ChatMessageSummary> Messages { get; set; } = new();
    }

    public class ChatMessageSummary
    {
        public int Id { get; set; }
        public string Role { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public bool HasActions { get; set; }
    }

    public class SessionDetailResponse
    {
        public Guid SessionId { get; set; }
        public string? UserPhone { get; set; }
        public string? UserName { get; set; }
        public string? UserLocation { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastActivityAt { get; set; }
        public int MessageCount { get; set; }
        public int ActionsCount { get; set; }
    }
}
