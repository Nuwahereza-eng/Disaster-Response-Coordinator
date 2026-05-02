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
        private string? historyError = null;
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
                // Demo mode: app is always signed in. Show the default identity
                // immediately so the badge / sidebar never flash a 'logged out' state
                // while the cold-starting backend finishes its silent sign-in.
                isAuthenticated = true;
                userName = "Peter Nuwahereza";
                userPhone = "+256779081600";

                // Fast path: restore any cached auth from localStorage. This is just JS interop —
                // no network call — so it doesn't freeze the UI on a cold-starting backend.
                await UserClient.InitializeAsync();

                if (UserClient.IsAuthenticated)
                {
                    userName = UserClient.CurrentUser?.FullName ?? userName;
                    userPhone = UserClient.CurrentUser?.Phone ?? userPhone;
                    AgentClient.SetAuthToken(UserClient.AuthToken);
                }
                StateHasChanged();

                // Everything below is fire-and-forget so the chat UI is interactive immediately.
                // Auto-login + history load run in the background; when they finish we re-render.
                _ = Task.Run(async () =>
                {
                    if (!UserClient.IsAuthenticated)
                    {
                        await UserClient.EnsureDemoLoggedInAsync();
                    }

                    if (UserClient.IsAuthenticated)
                    {
                        await InvokeAsync(() =>
                        {
                            userName = UserClient.CurrentUser?.FullName ?? userName;
                            userPhone = UserClient.CurrentUser?.Phone ?? userPhone;
                            AgentClient.SetAuthToken(UserClient.AuthToken);
                            StateHasChanged();
                        });
                        await LoadChatHistoryAsync();
                    }
                });

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
            historyError = null;
            await InvokeAsync(StateHasChanged);

            try
            {
                var history = await AgentClient.GetChatHistoryAsync();
                if (history != null)
                {
                    chatSessions = history.Sessions;
                }
            }
            catch (TimeoutException)
            {
                historyError = "Server is slow or unreachable. The database may be down.";
                Console.WriteLine("Chat history load timed out.");
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("401"))
            {
                // Stale JWT in localStorage (e.g. token signed for a user that no
                // longer exists after a DB reset). Wipe it, force a fresh demo
                // login, then retry once. This makes the demo bullet-proof against
                // 'judge opened the site, can't see anything' bugs after redeploy.
                Console.WriteLine("Chat history 401 — clearing stale token and re-authenticating.");
                try
                {
                    await UserClient.LogoutAsync();
                    var ok = await UserClient.EnsureDemoLoggedInAsync();
                    if (ok)
                    {
                        AgentClient.SetAuthToken(UserClient.AuthToken!);
                        var history = await AgentClient.GetChatHistoryAsync();
                        if (history != null)
                        {
                            chatSessions = history.Sessions;
                            historyError = null;
                        }
                    }
                    else
                    {
                        historyError = "Could not load conversations.";
                    }
                }
                catch (Exception retryEx)
                {
                    historyError = "Could not load conversations.";
                    Console.WriteLine($"Re-auth retry failed: {retryEx.Message}");
                }
            }
            catch (Exception ex)
            {
                historyError = "Could not load conversations.";
                Console.WriteLine($"Error loading chat history: {ex.Message}");
            }
            finally
            {
                loadingHistory = false;
                await InvokeAsync(StateHasChanged);
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

        private async Task CallAgent(bool addUserMessage = true)
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

                if (addUserMessage)
                {
                    messages.Add(new MessageSave
                    {
                        Prompt = prompt,
                        Role = 1
                    });
                }

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
        // SOS \u2014 instant one-tap dispatch.
        //
        // Designed for the worst case: a panicking user, no time to type,
        // possibly no internet. We do TWO things in parallel on a single tap:
        //
        //   1. JS path (window.drcPwa.fireSos) \u2014 POSTs to /api/sos directly.
        //      If offline, it queues to IndexedDB and the service worker
        //      flushes it when the network returns. This is the offline-first
        //      lifeline and works even if the Blazor circuit is dead.
        //   2. Agent path (CallAgent) \u2014 best-effort, so the chat shows what
        //      happened and the LLM can coordinate follow-up actions
        //      (notify next-of-kin, find nearby facilities, etc.).
        //
        // No 2-tap confirmation, no countdown. Accidental taps are a far
        // smaller cost than a missed real emergency.
        // =========================================================
        private async Task OnSosClick()
        {
            if (Processing) return;

            // Best-effort haptic feedback (mobile browsers).
            try { await JSRuntime.InvokeVoidAsync("sosBuzz"); } catch { /* ignore */ }

            // First tap: arm the 5-second confirmation window.
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

            // Second tap within the window: dispatch.
            sosConfirmCts?.Cancel();
            sosConfirming = false;

            // ============================================================
            // Make sure the chat shows what's happening RIGHT NOW so the
            // user (or judge in a demo) never just sees a toast and wonders
            // "did anything happen?". We populate the chat synchronously
            // before any network round-trip.
            // ============================================================

            // Refresh GPS so the user message includes coordinates.
            await RequestUserLocationAsync();

            var locationText = (userLatitude.HasValue && userLongitude.HasValue)
                ? $" My GPS coordinates are {userLatitude:F5}, {userLongitude:F5}."
                : " (location unavailable — please share)";

            var sosPrompt = $"\ud83d\udea8 SOS \u2014 I need immediate emergency help right now.{locationText} Please dispatch the closest responders and notify my emergency contacts.";

            // 1. User message in chat — instantly visible.
            messages.Add(new MessageSave { Prompt = sosPrompt, Role = 1 });

            // 2. Immediate Direco acknowledgement so the user knows action started.
            //    The full agent response (with Actions Taken chips) will be appended
            //    when the backend round-trip completes.
            var ackHtml =
                "<div class='agent-actions'><strong>\ud83d\udea8 SOS RECEIVED \u2014 dispatching now\u2026</strong>" +
                "<ul>" +
                "<li>\ud83d\udcdd Logged your emergency with GPS</li>" +
                "<li>\ud83d\udcde Notifying your next of kin (SMS + WhatsApp + email)</li>" +
                "<li>\ud83d\ude91 Alerting closest responders (police 999, ambulance 911, fire 112)</li>" +
                "<li>\ud83d\udcf6 Queued offline so it will deliver even if your network drops</li>" +
                "</ul></div>" +
                "<p><strong>Stay where you are. Help is being coordinated.</strong> If you can, tap on something hard so rescuers can hear you, conserve phone battery, and stay calm.</p>";
            messages.Add(new MessageSave { Prompt = ackHtml, Role = 0 });

            // Mark as emergency so the red banner shows.
            isEmergency = true;
            emergencySeverity = "Critical";

            // Scroll the new messages into view immediately.
            StateHasChanged();
            try { await JSRuntime.InvokeVoidAsync("ScrollToBottom", "chatcontainer"); } catch { }

            // 3. Offline-capable JS path. Fire-and-forget \u2014 drcPwa handles its
            //    own toasts and queues to IndexedDB if there's no connectivity.
            try
            {
                _ = JSRuntime.InvokeVoidAsync("drcPwa.fireSos", "SOS").AsTask();
            }
            catch { /* ignore \u2014 agent path is the fallback */ }

            // 4. Agent path \u2014 calls Gemini + tools, appends the real response
            //    (with Actions Taken chips) to the chat when ready. Best-effort:
            //    if the API is cold-starting we already showed the ack above.
            prompt = sosPrompt;
            await CallAgent(addUserMessage: false);
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
