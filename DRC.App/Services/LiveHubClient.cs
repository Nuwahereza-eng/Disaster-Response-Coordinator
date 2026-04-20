using Microsoft.AspNetCore.SignalR.Client;

namespace DRC.App.Services
{
    /// <summary>
    /// Thin wrapper around a SignalR HubConnection pointed at /hubs/live.
    /// Components subscribe to events via C# events and are kept in sync
    /// across reconnects automatically.
    /// </summary>
    public class LiveHubClient : IAsyncDisposable
    {
        private readonly string _hubUrl;
        private readonly ILogger<LiveHubClient> _logger;
        private HubConnection? _conn;

        public event Action<LiveEmergency>? EmergencyCreated;
        public event Action<LiveAction>? ActionDispatched;
        public event Action<LiveAlert>? AlertBroadcast;
        public event Action? Reconnected;
        public event Action? Disconnected;

        public bool IsConnected => _conn?.State == HubConnectionState.Connected;

        public LiveHubClient(IConfiguration config, ILogger<LiveHubClient> logger)
        {
            _logger = logger;
            var apiUrl = Environment.GetEnvironmentVariable("ApiUrl")
                ?? config["ApiUrl"]
                ?? "http://localhost:5099";
            if (!apiUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                apiUrl = "https://" + apiUrl;
            _hubUrl = apiUrl.TrimEnd('/') + "/hubs/live";
        }

        public async Task StartAsync(bool joinAdmins = false)
        {
            if (_conn != null && _conn.State != HubConnectionState.Disconnected) return;

            _conn = new HubConnectionBuilder()
                .WithUrl(_hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _conn.On<LiveEmergency>("emergencyCreated", e => EmergencyCreated?.Invoke(e));
            _conn.On<LiveAction>("actionDispatched", a => ActionDispatched?.Invoke(a));
            _conn.On<LiveAlert>("alertBroadcast", a => AlertBroadcast?.Invoke(a));

            _conn.Reconnected += _ =>
            {
                Reconnected?.Invoke();
                return Task.CompletedTask;
            };
            _conn.Closed += _ =>
            {
                Disconnected?.Invoke();
                return Task.CompletedTask;
            };

            try
            {
                await _conn.StartAsync();
                _logger.LogInformation("🟢 LiveHub connected to {Url}", _hubUrl);
                if (joinAdmins)
                    await _conn.InvokeAsync("JoinAdmins");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "🔴 LiveHub connection failed ({Url})", _hubUrl);
            }
        }

        public async Task JoinAdminsAsync()
        {
            if (_conn?.State == HubConnectionState.Connected)
                await _conn.InvokeAsync("JoinAdmins");
        }

        public async Task JoinSessionAsync(string sessionId)
        {
            if (_conn?.State == HubConnectionState.Connected)
                await _conn.InvokeAsync("JoinSession", sessionId);
        }

        public async ValueTask DisposeAsync()
        {
            if (_conn != null)
            {
                try { await _conn.DisposeAsync(); } catch { /* ignore */ }
                _conn = null;
            }
        }
    }

    // --- Payload DTOs (match the anonymous objects the API broadcasts) ---

    public class LiveEmergency
    {
        public int Id { get; set; }
        public string? Type { get; set; }
        public string? Severity { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public string? Phone { get; set; }
        public string? Channel { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? AssignedFacility { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class LiveAction
    {
        public string? SessionId { get; set; }
        public string? ToolName { get; set; }
        public string? Description { get; set; }
        public string? Status { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class LiveAlert
    {
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? Severity { get; set; }
        public string? Area { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
