using DRC.App.Models;
using DRC.App.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Data;
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
}
