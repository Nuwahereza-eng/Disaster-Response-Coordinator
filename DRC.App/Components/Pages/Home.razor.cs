using DRC.App.Models;
using DRC.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Data;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace DRC.App.Components.Pages
{
    public partial class Home
    {
        [Inject]
        private IJSRuntime JSRuntime { get; set; }

        [Inject]
        private AgentClientService AgentClient { get; set; }

        [Inject]
        private UserClientService UserClient { get; set; }

        [Inject]
        private NavigationManager NavigationManager { get; set; }

        [Inject]
        private IHttpClientFactory HttpFactory { get; set; }

        private List<MessageSave> messages = new List<MessageSave>();
        private List<AgentActionDisplay> recentActions = new List<AgentActionDisplay>();
        private string prompt = "";
        private string ErrorMessage = "";
        private bool Processing = false;
        private Guid? guid = null;
        private bool isEmergency = false;
        private string? emergencySeverity = null;
        private bool isAuthenticated = false;
        private string? userName = null;
        
        // Location tracking for emergency response
        private double? userLatitude = null;
        private double? userLongitude = null;
        private string? userPhone = null;
        private bool locationRequested = false;
        private string? locationStatus = null;
        
        // Chat history sidebar
        private bool sidebarOpen = true;
        private bool loadingHistory = false;
        private bool viewingHistory = false;
        private List<ChatSessionSummary> chatSessions = new();
        
        // Enter key handling
        private bool shouldPreventDefault = false;

        // SOS — two-tap confirm pattern (prevents accidental dispatch)
        private bool sosConfirming = false;
        private int sosSecondsLeft = 5;
        private CancellationTokenSource? sosConfirmCts;

        // Nearby Help map
        private bool mapCollapsed = false;
        private bool mapHydrated = false;
        private string mapStatus = "Waiting for location…";
        private bool mapError = false;
        private List<NearbyFacilityDto> nearbyFacilities = new();

        protected override async Task OnInitializedAsync()
        {
            // Auth will be checked in OnAfterRenderAsync when JS is available
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                // Initialize auth state from browser storage (requires JS)
                await UserClient.InitializeAsync();
                
                // Check if user is authenticated and sync auth token to agent client
                if (UserClient.IsAuthenticated)
                {
                    isAuthenticated = true;
                    userName = UserClient.CurrentUser?.FullName;
                    userPhone = UserClient.CurrentUser?.Phone;
                    // Sync the auth token to AgentClientService so sessions are linked to the user
                    AgentClient.SetAuthToken(UserClient.AuthToken);
                    
                    // Load chat history
                    await LoadChatHistoryAsync();
                }
                else
                {
                    // Auto-login with demo account for seamless experience
                    await AutoLoginAsync();
                }
                
                // CRITICAL: Request user location immediately for emergency response
                await RequestUserLocationAsync();
                StateHasChanged();

                // Fire-and-forget: populate Nearby Help map once we have coords
                _ = HydrateMapAsync();
            }
            
            try
            {
                await JSRuntime.InvokeVoidAsync("ScrollToBottom", "chatcontainer");
            }
            catch
            {
                // Ignore if fails
            }
        }

        private async Task AutoLoginAsync()
        {
            try
            {
                var result = await UserClient.LoginAsync("drc@africastalking.ug", "Judge2026!");
                if (result != null)
                {
                    isAuthenticated = true;
                    userName = UserClient.CurrentUser?.FullName;
                    userPhone = UserClient.CurrentUser?.Phone;
                    AgentClient.SetAuthToken(UserClient.AuthToken);
                    await LoadChatHistoryAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Auto-login failed: {ex.Message}");
            }
        }

        private async Task LoadChatHistoryAsync()
        {
            if (!isAuthenticated) return;
            
            loadingHistory = true;
            StateHasChanged();
            
            try
            {
                var history = await AgentClient.GetChatHistoryAsync();
                if (history != null)
                {
                    chatSessions = history.Sessions;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading chat history: {ex.Message}");
            }
            finally
            {
                loadingHistory = false;
                StateHasChanged();
            }
        }

        private void ToggleSidebar()
        {
            sidebarOpen = !sidebarOpen;
        }

        private void StartNewChat()
        {
            prompt = "";
            messages = new List<MessageSave>();
            recentActions = new List<AgentActionDisplay>();
            ErrorMessage = "";
            guid = null;
            isEmergency = false;
            emergencySeverity = null;
            viewingHistory = false;
            StateHasChanged();
        }

        private async Task LoadSession(ChatSessionSummary session)
        {
            messages = new List<MessageSave>();
            recentActions = new List<AgentActionDisplay>();
            guid = session.SessionId;
            viewingHistory = true;
            
            // Load messages from the session
            foreach (var msg in session.Messages)
            {
                messages.Add(new MessageSave
                {
                    Prompt = msg.Content,
                    Role = msg.Role == "user" ? 1 : 0
                });
            }
            
            StateHasChanged();
            
            try
            {
                await JSRuntime.InvokeVoidAsync("ScrollToBottom", "chatcontainer");
            }
            catch { }
        }

        private string FormatSessionTime(DateTime time)
        {
            var diff = DateTime.UtcNow - time;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return time.ToString("MMM d");
        }

        private void Restart()
        {
            StartNewChat();
        }

        private async Task HandleKeyPress(KeyboardEventArgs e)
        {
            if (e.Key == "Enter" && !e.ShiftKey)
            {
                shouldPreventDefault = true;
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    await CallAgent();
                }
            }
            else
            {
                shouldPreventDefault = false;
            }
        }

        private async Task RequestUserLocationAsync()
        {
            if (locationRequested) return;
            locationRequested = true;
            locationStatus = "Requesting location...";
            
            try
            {
                var position = await JSRuntime.InvokeAsync<GeolocationResult?>("getGeolocation");
                if (position != null && position.Success)
                {
                    userLatitude = position.Latitude;
                    userLongitude = position.Longitude;
                    locationStatus = "📍 Location acquired";
                }
                else
                {
                    locationStatus = position?.Error ?? "Location unavailable";
                }
            }
            catch
            {
                locationStatus = "Location access denied";
            }
        }

        private void ToggleMap()
        {
            mapCollapsed = !mapCollapsed;
            if (!mapCollapsed)
            {
                // Re-render after the canvas div is back in the DOM
                _ = InvokeAsync(async () =>
                {
                    await Task.Delay(60);
                    if (userLatitude.HasValue && userLongitude.HasValue)
                    {
                        await RenderMapAsync();
                    }
                    else
                    {
                        _ = HydrateMapAsync();
                    }
                });
            }
        }

        private async Task HydrateMapAsync()
        {
            // Wait until we actually have a GPS fix (up to ~15s)
            for (int i = 0; i < 30 && (userLatitude == null || userLongitude == null); i++)
            {
                await Task.Delay(500);
            }

            if (userLatitude == null || userLongitude == null)
            {
                mapStatus = "Enable location to see nearby help";
                mapError = true;
                await InvokeAsync(StateHasChanged);
                return;
            }

            await LoadNearbyFacilitiesAsync();
            await RenderMapAsync();
        }

        private async Task LoadNearbyFacilitiesAsync()
        {
            try
            {
                mapStatus = "Loading nearby facilities\u2026";
                mapError = false;
                await InvokeAsync(StateHasChanged);

                var http = HttpFactory.CreateClient("UserApi");
                var url = $"api/Facilities/nearby?lat={userLatitude}&lng={userLongitude}&radiusKm=30&limit=25";
                var list = await http.GetFromJsonAsync<List<NearbyFacilityDto>>(url);
                nearbyFacilities = list ?? new();
                mapStatus = nearbyFacilities.Count > 0
                    ? $"Nearest: {nearbyFacilities[0].Name} \u2022 {nearbyFacilities[0].DistanceKm} km"
                    : "No facilities within 30 km";
            }
            catch (Exception ex)
            {
                nearbyFacilities = new();
                mapStatus = "Could not load facilities";
                mapError = true;
                Console.WriteLine($"Facility load failed: {ex.Message}");
            }
            await InvokeAsync(StateHasChanged);
        }

        private async Task RenderMapAsync()
        {
            if (userLatitude == null || userLongitude == null) return;
            if (mapCollapsed) return;
            try
            {
                var payload = nearbyFacilities.Select(f => new
                {
                    id = f.Id,
                    name = f.Name,
                    type = f.Type,
                    address = f.Address,
                    latitude = f.Latitude,
                    longitude = f.Longitude,
                    phone = f.Phone,
                    distanceKm = f.DistanceKm
                });
                await JSRuntime.InvokeVoidAsync("drcMap.render", "nearbyMapCanvas",
                    userLatitude.Value, userLongitude.Value, payload);
                mapHydrated = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Map render failed: {ex.Message}");
            }
        }

        private async Task CallAgent()
        {
            try
            {
                Processing = true;
                StateHasChanged();
                ErrorMessage = "";
                
                // Retry getting location if not available yet
                if (userLatitude == null || userLongitude == null)
                {
                    await RequestUserLocationAsync();
                }

                var response = await AgentClient.Conversation(prompt, guid, userLatitude, userLongitude, userPhone);

                messages.Add(new MessageSave
                {
                    Prompt = prompt,
                    Role = 1
                });

                // Track actions taken
                var actionsHtml = "";
                if (response.ActionsTaken.Any())
                {
                    actionsHtml = "<div class='agent-actions'><strong>🤖 Actions Taken:</strong><ul>";
                    foreach (var action in response.ActionsTaken)
                    {
                        var icon = GetActionIcon(action.ToolName);
                        actionsHtml += $"<li>{icon} {action.Description} <span class='action-status status-{action.Status.ToLower()}'>{action.Status}</span></li>";
                        
                        recentActions.Add(new AgentActionDisplay
                        {
                            Id = action.Id,
                            ToolName = action.ToolName,
                            Description = action.Description,
                            Status = action.Status,
                            Timestamp = DateTime.Now
                        });
                    }
                    actionsHtml += "</ul></div>";
                }

                messages.Add(new MessageSave
                {
                    Prompt = actionsHtml + response.Response,
                    Role = 0
                });

                guid = response.Guid;
                isEmergency = response.IsEmergency;
                emergencySeverity = response.Severity;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                prompt = "";
                Processing = false;
                StateHasChanged();
            }
        }

        private string GetActionIcon(string toolName)
        {
            return toolName switch
            {
                "request_emergency_services" => "bi-exclamation-triangle-fill",
                "find_nearby_facilities" => "bi-building",
                "register_for_shelter" => "bi-house-fill",
                "notify_emergency_contacts" => "bi-telephone-fill",
                "request_evacuation" => "bi-truck",
                "check_disaster_alerts" => "bi-exclamation-triangle",
                "get_safety_instructions" => "bi-clipboard-check",
                _ => "bi-check-circle-fill"
            };
        }

        // =========================================================
        // SOS — one floating button, two-tap confirmation pattern.
        // 1st tap  → 5-second "tap again to confirm" countdown + haptic.
        // 2nd tap  → dispatches an emergency with GPS to the agent.
        // No tap   → automatically cancels after 5s.
        // =========================================================
        private async Task OnSosClick()
        {
            if (Processing) return;

            // Best-effort haptic feedback (mobile browsers)
            try { await JSRuntime.InvokeVoidAsync("sosBuzz"); } catch { /* ignore */ }

            if (!sosConfirming)
            {
                sosConfirming = true;
                sosSecondsLeft = 5;
                sosConfirmCts?.Cancel();
                sosConfirmCts = new CancellationTokenSource();
                _ = RunSosCountdown(sosConfirmCts.Token);
                StateHasChanged();
                return;
            }

            // Second tap: dispatch emergency
            sosConfirmCts?.Cancel();
            sosConfirming = false;
            StateHasChanged();
            await DispatchSosAsync();
        }

        private async Task RunSosCountdown(CancellationToken ct)
        {
            try
            {
                while (sosSecondsLeft > 0 && !ct.IsCancellationRequested)
                {
                    await Task.Delay(1000, ct);
                    sosSecondsLeft--;
                    await InvokeAsync(StateHasChanged);
                }
                if (!ct.IsCancellationRequested)
                {
                    sosConfirming = false;
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (TaskCanceledException) { /* user tapped again or navigated */ }
        }

        private async Task DispatchSosAsync()
        {
            // Make sure we have the latest location before dispatching
            await RequestUserLocationAsync();

            var locationText = (userLatitude.HasValue && userLongitude.HasValue)
                ? $" My GPS coordinates are {userLatitude:F5}, {userLongitude:F5}."
                : "";

            prompt = $"🚨 SOS — I need immediate emergency help.{locationText} Please dispatch the closest responders now and notify my emergency contacts.";
            await CallAgent();
        }
    }

    public class AgentActionDisplay
    {
        public string Id { get; set; } = "";
        public string ToolName { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
    
    public class GeolocationResult
    {
        public bool Success { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Error { get; set; }
    }

    public class NearbyFacilityDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string? Address { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? Phone { get; set; }
        public int? Capacity { get; set; }
        public int? CurrentOccupancy { get; set; }
        public bool Is24Hours { get; set; }
        public double DistanceKm { get; set; }
    }
}
