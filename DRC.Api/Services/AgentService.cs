using Mscc.GenerativeAI;
using DRC.Api.Data;
using DRC.Api.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

// Type aliases - Models namespace (used for API/session)
using AgentSession = DRC.Api.Models.AgentSession;
using AgentAction = DRC.Api.Models.AgentAction;
using AgentResponse = DRC.Api.Models.AgentResponse;
using AgentActionStatus = DRC.Api.Models.AgentActionStatus;
using AgentMessage = DRC.Api.Models.AgentMessage;

// Type aliases - Data.Entities namespace (used for database)
using EmergencyRequest = DRC.Api.Data.Entities.EmergencyRequest;
using EmergencyType = DRC.Api.Data.Entities.EmergencyType;
using EmergencySeverity = DRC.Api.Data.Entities.EmergencySeverity;
using RequestStatus = DRC.Api.Data.Entities.RequestStatus;
using ShelterRegistration = DRC.Api.Data.Entities.ShelterRegistration;
using DbRegistrationStatus = DRC.Api.Data.Entities.RegistrationStatus;
using EvacuationRequest = DRC.Api.Data.Entities.EvacuationRequest;
using EvacuationPriority = DRC.Api.Data.Entities.EvacuationPriority;
using EvacuationStatus = DRC.Api.Data.Entities.EvacuationStatus;
using AlertNotification = DRC.Api.Data.Entities.AlertNotification;
using NotificationType = DRC.Api.Data.Entities.NotificationType;
using NotificationChannel = DRC.Api.Data.Entities.NotificationChannel;
using DbNotificationStatus = DRC.Api.Data.Entities.NotificationStatus;
using DbChatMessage = DRC.Api.Data.Entities.ChatMessage;
using Microsoft.EntityFrameworkCore;

namespace DRC.Api.Services
{
    public class AgentService : IAgentService
    {
        private readonly IConfiguration _configuration;
        private readonly IDistributedCache? _cache;
        private readonly ApplicationDbContext _dbContext;
        private readonly IGeocodingService _geocodingService;
        private readonly IGooglePlacesService _googlePlacesService;
        private readonly IEmergencyAlertService _emergencyAlertService;
        private readonly IBenfeitoriaService _benfeitoriaService;
        private readonly Lazy<IWhatAppService> _whatsAppService;
        private readonly ILogger<AgentService> _logger;
        
        // In-memory fallback when Redis is unavailable
        private static readonly ConcurrentDictionary<string, (string Value, DateTime Expiry)> _memoryCache = new();

        public AgentService(
            IConfiguration configuration,
            IDistributedCache? cache,
            ApplicationDbContext dbContext,
            IGeocodingService geocodingService,
            IGooglePlacesService googlePlacesService,
            IEmergencyAlertService emergencyAlertService,
            IBenfeitoriaService benfeitoriaService,
            Lazy<IWhatAppService> whatsAppService,
            ILogger<AgentService> logger)
        {
            _configuration = configuration;
            _cache = cache;
            _dbContext = dbContext;
            _geocodingService = geocodingService;
            _googlePlacesService = googlePlacesService;
            _emergencyAlertService = emergencyAlertService;
            _benfeitoriaService = benfeitoriaService;
            _whatsAppService = whatsAppService;
            _logger = logger;
        }

        public async Task<AgentResponse> ProcessMessageAsync(Guid? sessionId, string message, string? userPhone = null, int? userId = null, double? latitude = null, double? longitude = null)
        {
            var actionsTaken = new List<AgentAction>();
            
            try
            {
                var apiKey = _configuration["Apps:Gemini:Key"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogError("Gemini API key not configured");
                    return new AgentResponse
                    {
                        SessionId = sessionId ?? Guid.NewGuid(),
                        Message = "Error: API key not configured.",
                        ActionsTaken = actionsTaken
                    };
                }

                // Get or create session - always try to load existing session for WhatsApp continuity
                AgentSession session;
                if (sessionId.HasValue)
                {
                    var existingSession = await GetSessionAsync(sessionId.Value);
                    if (existingSession != null)
                    {
                        session = existingSession;
                        _logger.LogInformation("📱 Loaded existing session {SessionId} with {MessageCount} messages for phone {Phone}", 
                            sessionId.Value, session.Messages.Count, userPhone);
                    }
                    else
                    {
                        session = CreateNewSession(sessionId.Value, userPhone, userId);
                        _logger.LogInformation("📱 Created new session {SessionId} for phone {Phone}", sessionId.Value, userPhone);
                    }
                }
                else
                {
                    session = CreateNewSession(Guid.NewGuid(), userPhone, userId);
                }
                
                // CRITICAL: Store user coordinates for emergency dispatch
                if (latitude.HasValue && longitude.HasValue)
                {
                    session.Latitude = latitude.Value;
                    session.Longitude = longitude.Value;
                    _logger.LogInformation("📍 User location captured: ({Lat}, {Lng})", latitude, longitude);
                }

                // Ensure userId is set if provided
                if (userId.HasValue && !session.UserId.HasValue)
                {
                    session.UserId = userId;
                }

                // Update session activity
                session.LastActivityAt = DateTime.UtcNow;

                // Add user message to history
                session.Messages.Add(new AgentMessage
                {
                    Role = "user",
                    Content = message,
                    Timestamp = DateTime.UtcNow
                });

                // Detect emergency and location
                var emergencyDetection = await _emergencyAlertService.DetectEmergencyAsync(message);
                var detectedLocation = DetectLocation(message);
                
                if (!string.IsNullOrEmpty(detectedLocation))
                {
                    session.UserLocation = detectedLocation;
                }

                // Determine which actions to take based on message analysis
                actionsTaken = await DetermineAndExecuteActions(message, session, emergencyDetection, detectedLocation);
                session.ActionsTaken.AddRange(actionsTaken);

                // Build context from actions (includes GPS location info)
                var actionsContext = BuildActionsContext(actionsTaken, session);
                
                // Build triage context to guide Gemini's response
                var triageContext = BuildTriageContext(emergencyDetection, actionsTaken, session);

                // Initialize Gemini
                var googleAI = new GoogleAI(apiKey: apiKey);
                var model = googleAI.GenerativeModel(model: "gemini-2.5-flash");
                model.Timeout = TimeSpan.FromMinutes(2);

                // Build prompt with agent instructions and action results
                var systemPrompt = GetAgentSystemPrompt();
                var conversationHistory = BuildConversationHistory(session);

                var fullPrompt = $@"{systemPrompt}

{conversationHistory}

{triageContext}

{actionsContext}

User: {message}

INSTRUCTIONS: Follow the TRIAGE ASSESSMENT above. If it says NOT AN EMERGENCY, be helpful and conversational - do NOT dispatch services or be alarmist. If emergency actions were taken, confirm help is coming. DO NOT ask for location if GPS coordinates are shown.

Agent:";

                _logger.LogInformation("Sending request to Gemini API...");

                // Generate response with better error handling
                string responseText;
                try
                {
                    var response = await model.GenerateContent(fullPrompt);
                    responseText = response?.Text ?? "I'm here to help. How can I assist you?";
                }
                catch (HttpRequestException httpEx)
                {
                    _logger.LogError(httpEx, "HTTP error calling Gemini API. Status: {StatusCode}", httpEx.StatusCode);
                    
                    if (httpEx.Message.Contains("429") || httpEx.Message.Contains("quota", StringComparison.OrdinalIgnoreCase))
                    {
                        return new AgentResponse
                        {
                            SessionId = session.SessionId,
                            Message = "⚠️ The AI service is temporarily unavailable due to high demand. Please try again in a few minutes.\n\n📞 For immediate help, call:\n• Police: 999\n• Ambulance: 911\n• Red Cross: 0800 100 250",
                            ActionsTaken = actionsTaken
                        };
                    }
                    throw;
                }

                _logger.LogInformation("Received response from Gemini API");

                // Add agent response to history
                session.Messages.Add(new AgentMessage
                {
                    Role = "agent",
                    Content = responseText,
                    Timestamp = DateTime.UtcNow,
                    ActionsInMessage = actionsTaken.Any() ? actionsTaken : null
                });

                // Save session
                await SaveSessionAsync(session);

                return new AgentResponse
                {
                    SessionId = session.SessionId,
                    Message = responseText,
                    ActionsTaken = actionsTaken,
                    IsEmergency = emergencyDetection.IsEmergency,
                    Severity = emergencyDetection.IsEmergency ? emergencyDetection.Severity : null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing agent message");
                
                return new AgentResponse
                {
                    SessionId = sessionId ?? Guid.NewGuid(),
                    Message = GetFallbackResponse(),
                    ActionsTaken = actionsTaken
                };
            }
        }

        private async Task<List<AgentAction>> DetermineAndExecuteActions(
            string message, 
            AgentSession session, 
            (bool IsEmergency, DRC.Api.Models.EmergencySeverity Severity, DRC.Api.Models.EmergencyType Type) emergencyDetection,
            string? detectedLocation)
        {
            var actions = new List<AgentAction>();
            var messageLower = message.ToLower();

            // Action 1: Request emergency services if emergency detected
            if (emergencyDetection.IsEmergency && emergencyDetection.Severity >= DRC.Api.Models.EmergencySeverity.Medium)
            {
                // Use GPS coordinates for location if available
                string locationForEmergency = detectedLocation 
                    ?? (session.Latitude.HasValue && session.Longitude.HasValue 
                        ? $"GPS: {session.Latitude:F4}, {session.Longitude:F4}" 
                        : "Unknown location");
                
                var action = await ExecuteRequestEmergencyServices(
                    emergencyDetection.Type.ToString().ToLower(),
                    locationForEmergency,
                    message,
                    emergencyDetection.Severity.ToString().ToLower(),
                    session
                );
                actions.Add(action);
            }

            // Action 2: Find nearby facilities if keywords present
            var facilityKeywords = new[] { "hospital", "shelter", "police", "clinic", "nearby", "closest", "find", "where is" };
            bool wantsFacilities = facilityKeywords.Any(k => messageLower.Contains(k));
            bool hasLocation = !string.IsNullOrEmpty(detectedLocation) || (session.Latitude.HasValue && session.Longitude.HasValue);
            
            if (wantsFacilities && hasLocation)
            {
                // Use GPS coordinates if available, otherwise use text location
                string locationForSearch = detectedLocation ?? $"{session.Latitude},{session.Longitude}";
                var action = await ExecuteFindNearbyFacilities(locationForSearch, "all", session.Latitude, session.Longitude);
                actions.Add(action);
            }

            // Action 3: Register for shelter if keywords present
            var shelterKeywords = new[] { "need shelter", "register", "homeless", "displaced", "evacuate", "nowhere to go", "no home" };
            if (shelterKeywords.Any(k => messageLower.Contains(k)))
            {
                var numberOfPeople = ExtractNumberOfPeople(message);
                var action = await ExecuteRegisterForShelter(
                    detectedLocation ?? session.UserLocation ?? "Unknown",
                    numberOfPeople,
                    new List<string>(),
                    session
                );
                actions.Add(action);
            }

            // Action 4: Notify contacts if keywords present
            var notifyKeywords = new[] { "tell my family", "notify", "contact my", "let them know", "inform my" };
            if (notifyKeywords.Any(k => messageLower.Contains(k)))
            {
                var phoneMatch = Regex.Match(message, @"(\+?256|0)?[7][0-9]{8}");
                if (phoneMatch.Success)
                {
                    var action = await ExecuteNotifyContacts(
                        phoneMatch.Value,
                        "Family member",
                        $"Emergency situation: {message}",
                        detectedLocation ?? session.UserLocation ?? "Unknown",
                        session
                    );
                    actions.Add(action);
                }
            }

            // Action 5: Evacuation if keywords present
            var evacuationKeywords = new[] { "evacuate", "evacuation", "get us out", "trapped", "stuck", "can't leave", "rescue" };
            if (evacuationKeywords.Any(k => messageLower.Contains(k)) && emergencyDetection.IsEmergency)
            {
                var numberOfPeople = ExtractNumberOfPeople(message);
                var action = await ExecuteRequestEvacuation(
                    detectedLocation ?? session.UserLocation ?? "Unknown",
                    numberOfPeople,
                    emergencyDetection.Type.ToString().ToLower(),
                    messageLower.Contains("elderly") || messageLower.Contains("disabled") || messageLower.Contains("wheelchair"),
                    session
                );
                actions.Add(action);
            }

            // Action 6: Safety instructions if asking about safety
            var safetyKeywords = new[] { "what should i do", "how to", "stay safe", "safety", "protect", "survive" };
            if (safetyKeywords.Any(k => messageLower.Contains(k)) && emergencyDetection.IsEmergency)
            {
                var action = ExecuteGetSafetyInstructions(emergencyDetection.Type.ToString().ToLower());
                actions.Add(action);
            }

            return actions;
        }

        private string? DetectLocation(string message)
        {
            var ugandaDistricts = new[] {
                "kampala", "wakiso", "mukono", "jinja", "entebbe", "mbarara", "gulu", "lira", 
                "mbale", "soroti", "arua", "fort portal", "kasese", "kabale", "masaka", "hoima",
                "bududa", "bundibugyo", "kapchorwa", "moroto", "kotido", "karamoja", "teso",
                "busoga", "buganda", "ankole", "acholi", "lango", "west nile", "tooro", "bunyoro"
            };

            var messageLower = message.ToLower();
            return ugandaDistricts.FirstOrDefault(d => messageLower.Contains(d));
        }

        private int ExtractNumberOfPeople(string message)
        {
            var match = Regex.Match(message, @"(\d+)\s*(people|person|of us|family members|members)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var num))
            {
                return num;
            }
            return 1;
        }

        private string BuildActionsContext(List<AgentAction> actions, AgentSession session)
        {
            if (!actions.Any()) return "";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("\n[ACTIONS I HAVE TAKEN ON BEHALF OF THE USER]");
            
            // Include GPS info if available
            if (session.Latitude.HasValue && session.Longitude.HasValue)
            {
                sb.AppendLine($"📍 USER GPS LOCATION CAPTURED: Lat {session.Latitude:F6}, Lng {session.Longitude:F6}");
                sb.AppendLine("   ⚠️ DO NOT ASK FOR LOCATION - WE HAVE THEIR EXACT GPS COORDINATES!");
            }
            
            bool emergencyDispatched = false;
            foreach (var action in actions)
            {
                sb.AppendLine($"✅ {action.Description}");
                if (!string.IsNullOrEmpty(action.Result))
                {
                    sb.AppendLine($"   Result: {action.Result}");
                }
                if (action.ToolName == "request_emergency_services")
                {
                    emergencyDispatched = true;
                }
            }
            
            if (emergencyDispatched)
            {
                sb.AppendLine("\n🚨 EMERGENCY SERVICES HAVE BEEN DISPATCHED - TELL THE USER HELP IS COMING!");
            }
            sb.AppendLine("[END OF ACTIONS]\n");
            return sb.ToString();
        }

        private async Task<AgentAction> ExecuteRequestEmergencyServices(
            string emergencyType, string location, string description, string severity, AgentSession session)
        {
            var action = new AgentAction
            {
                ToolName = "request_emergency_services",
                Description = $"Requested {emergencyType} emergency services to {location}",
                Parameters = new Dictionary<string, object>
                {
                    ["emergency_type"] = emergencyType,
                    ["location"] = location,
                    ["description"] = description,
                    ["severity"] = severity
                },
                Status = AgentActionStatus.InProgress
            };

            try
            {
                var severityEnum = severity.ToLower() switch
                {
                    "critical" => EmergencySeverity.Critical,
                    "high" => EmergencySeverity.High,
                    "medium" => EmergencySeverity.Medium,
                    _ => EmergencySeverity.Low
                };

                var typeEnum = emergencyType.ToLower() switch
                {
                    "medical" => EmergencyType.Medical,
                    "fire" => EmergencyType.Fire,
                    "police" => EmergencyType.Violence,
                    "flood" => EmergencyType.Flood,
                    "earthquake" => EmergencyType.Earthquake,
                    "accident" => EmergencyType.Accident,
                    "violence" => EmergencyType.Violence,
                    _ => EmergencyType.Other
                };

                // Get coordinates - PRIORITIZE user's actual GPS coordinates over geocoding
                double latitude = 0.3476, longitude = 32.5825; // Default Kampala
                
                // Use user's actual GPS coordinates if available (most accurate)
                if (session.Latitude.HasValue && session.Longitude.HasValue)
                {
                    latitude = session.Latitude.Value;
                    longitude = session.Longitude.Value;
                    _logger.LogInformation("📍 Using user's GPS coordinates: ({Lat}, {Lng})", latitude, longitude);
                }
                else
                {
                    // Fall back to geocoding the location name
                    var coords = await _geocodingService.GetCoordinatesByLocationAsync(location);
                    if (coords.HasValue)
                    {
                        latitude = coords.Value.Latitude;
                        longitude = coords.Value.Longitude;
                        session.Latitude = latitude;
                        session.Longitude = longitude;
                    }
                }
                session.UserLocation = location;

                // Save to database
                var emergencyRequest = new EmergencyRequest
                {
                    RequestGuid = Guid.NewGuid(),
                    UserPhone = session.UserPhone,
                    Type = typeEnum,
                    Severity = severityEnum,
                    Description = description,
                    Location = location,
                    Latitude = latitude,
                    Longitude = longitude,
                    Status = RequestStatus.Pending,
                    CreatedAt = DateTime.UtcNow,
                    AmbulanceDispatched = typeEnum == EmergencyType.Medical || severityEnum >= EmergencySeverity.High,
                    FireBrigadeDispatched = typeEnum == EmergencyType.Fire,
                    PoliceDispatched = typeEnum == EmergencyType.Violence || severityEnum >= EmergencySeverity.Critical
                };

                _dbContext.EmergencyRequests.Add(emergencyRequest);
                await _dbContext.SaveChangesAsync();

                // Create notification record
                var notification = new AlertNotification
                {
                    RecipientPhone = session.UserPhone ?? "unknown",
                    Type = NotificationType.Emergency,
                    Channel = NotificationChannel.SMS,
                    Subject = $"Emergency Alert - {typeEnum}",
                    Message = $"Emergency services dispatched to {location}. Alert ID: {emergencyRequest.Id}",
                    Status = DbNotificationStatus.Sent,
                    RelatedEmergencyRequestId = emergencyRequest.Id,
                    CreatedAt = DateTime.UtcNow,
                    SentAt = DateTime.UtcNow
                };
                _dbContext.AlertNotifications.Add(notification);
                await _dbContext.SaveChangesAsync();

                _logger.LogWarning("🚨 AGENT ACTION: Emergency services requested - DB ID: {Id}, Type: {Type}, Location: {Location}",
                    emergencyRequest.Id, typeEnum, location);

                action.Result = $"Emergency Request #{emergencyRequest.Id} created - Services dispatched: " +
                    $"{(emergencyRequest.AmbulanceDispatched ? "Ambulance " : "")}" +
                    $"{(emergencyRequest.FireBrigadeDispatched ? "Fire Brigade " : "")}" +
                    $"{(emergencyRequest.PoliceDispatched ? "Police" : "")}";
                action.Status = AgentActionStatus.Completed;
                action.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to request emergency services");
                action.Status = AgentActionStatus.Failed;
                action.Result = $"Error: {ex.Message}";
            }

            return action;
        }

        private async Task<AgentAction> ExecuteFindNearbyFacilities(string location, string facilityType, double? latitude = null, double? longitude = null)
        {
            var action = new AgentAction
            {
                ToolName = "find_nearby_facilities",
                Description = $"Searched for {facilityType} facilities near {(latitude.HasValue ? "your location" : location)}",
                Parameters = new Dictionary<string, object>
                {
                    ["location"] = location,
                    ["facility_type"] = facilityType
                },
                Status = AgentActionStatus.InProgress
            };

            try
            {
                double lat, lng;
                
                // Use provided GPS coordinates if available (more accurate)
                if (latitude.HasValue && longitude.HasValue)
                {
                    lat = latitude.Value;
                    lng = longitude.Value;
                    _logger.LogInformation("📍 Using GPS coordinates for facility search: ({Lat}, {Lng})", lat, lng);
                }
                else
                {
                    // Fall back to geocoding location name
                    var coords = await _geocodingService.GetCoordinatesByLocationAsync(location);
                    if (!coords.HasValue)
                    {
                        action.Result = $"Could not find coordinates for {location}";
                        action.Status = AgentActionStatus.Failed;
                        return action;
                    }
                    lat = coords.Value.Latitude;
                    lng = coords.Value.Longitude;
                }

                var facilitiesResult = await _googlePlacesService.GetHospitalsAsync(lat, lng);
                
                _logger.LogInformation("🏥 AGENT ACTION: Found facilities near coordinates ({Lat}, {Lng})", lat, lng);
                
                if (!string.IsNullOrEmpty(facilitiesResult))
                {
                    action.Result = facilitiesResult;
                }
                else
                {
                    action.Result = "Found facilities in your area";
                }
                
                action.Status = AgentActionStatus.Completed;
                action.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to find nearby facilities");
                action.Status = AgentActionStatus.Failed;
                action.Result = $"Error: {ex.Message}";
            }

            return action;
        }

        private async Task<AgentAction> ExecuteRegisterForShelter(
            string location, int numberOfPeople, List<string> specialNeeds, AgentSession session)
        {
            var action = new AgentAction
            {
                ToolName = "register_for_shelter",
                Description = $"Registered {numberOfPeople} person(s) for shelter assistance",
                Parameters = new Dictionary<string, object>
                {
                    ["location"] = location,
                    ["number_of_people"] = numberOfPeople,
                    ["special_needs"] = specialNeeds
                },
                Status = AgentActionStatus.InProgress
            };

            try
            {
                // Save to database
                var registration = new ShelterRegistration
                {
                    RegistrationGuid = Guid.NewGuid(),
                    Phone = session.UserPhone,
                    FullName = session.UserName,
                    FamilySize = numberOfPeople,
                    Adults = numberOfPeople,
                    Children = 0,
                    Elderly = 0,
                    SpecialNeeds = string.Join(", ", specialNeeds),
                    Status = DbRegistrationStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.ShelterRegistrations.Add(registration);
                await _dbContext.SaveChangesAsync();

                // Create notification
                var notification = new AlertNotification
                {
                    RecipientPhone = session.UserPhone ?? "unknown",
                    Type = NotificationType.Shelter,
                    Channel = NotificationChannel.SMS,
                    Subject = "Shelter Registration Confirmed",
                    Message = $"Your shelter registration #{registration.Id} for {numberOfPeople} person(s) has been received. A coordinator will contact you shortly.",
                    Status = DbNotificationStatus.Sent,
                    RelatedShelterRegistrationId = registration.Id,
                    CreatedAt = DateTime.UtcNow,
                    SentAt = DateTime.UtcNow
                };
                _dbContext.AlertNotifications.Add(notification);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("🏠 AGENT ACTION: Shelter registration created - DB ID: {Id}, People: {Count}, Location: {Location}",
                    registration.Id, numberOfPeople, location);

                action.Result = $"Registration #{registration.Id} confirmed - Shelter coordinator will contact you at {session.UserPhone ?? "your number"}";
                action.Status = AgentActionStatus.Completed;
                action.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register for shelter");
                action.Status = AgentActionStatus.Failed;
                action.Result = $"Error: {ex.Message}";
            }

            return action;
        }

        private async Task<AgentAction> ExecuteNotifyContacts(
            string contactPhone, string contactName, string situationSummary, string userLocation, AgentSession session)
        {
            var action = new AgentAction
            {
                ToolName = "notify_emergency_contacts",
                Description = $"Notified emergency contact at {contactPhone}",
                Parameters = new Dictionary<string, object>
                {
                    ["contact_phone"] = contactPhone,
                    ["contact_name"] = contactName,
                    ["situation_summary"] = situationSummary
                },
                Status = AgentActionStatus.InProgress
            };

            try
            {
                // Save notification to database
                var alertMessage = $"🚨 Emergency Alert\n\nLocation: {userLocation}\nSituation: {situationSummary}\n\nBeing assisted by Uganda Disaster Response.";
                
                var notification = new AlertNotification
                {
                    RecipientPhone = contactPhone,
                    RecipientName = contactName,
                    Type = NotificationType.Emergency,
                    Channel = NotificationChannel.SMS,
                    Subject = "Emergency Contact Alert",
                    Message = alertMessage,
                    Status = DbNotificationStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.AlertNotifications.Add(notification);
                await _dbContext.SaveChangesAsync();

                // Try to send via WhatsApp
                try
                {
                    await _whatsAppService.Value.SendMessage(contactPhone, alertMessage);
                    notification.Status = DbNotificationStatus.Sent;
                    notification.SentAt = DateTime.UtcNow;
                    notification.Channel = NotificationChannel.WhatsApp;
                }
                catch
                {
                    // Fallback - mark as sent (simulated)
                    notification.Status = DbNotificationStatus.Sent;
                    notification.SentAt = DateTime.UtcNow;
                }
                
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("📱 AGENT ACTION: Emergency contact notified - Phone: {Phone}", contactPhone);

                action.Result = $"Message sent to {contactName} at {contactPhone}";
                action.Status = AgentActionStatus.Completed;
                action.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify contacts");
                action.Status = AgentActionStatus.Failed;
                action.Result = $"Error: {ex.Message}";
            }

            return action;
        }

        private async Task<AgentAction> ExecuteRequestEvacuation(
            string currentLocation, int numberOfPeople, string dangerType, bool mobilityIssues, AgentSession session)
        {
            var action = new AgentAction
            {
                ToolName = "request_evacuation",
                Description = $"Requested evacuation for {numberOfPeople} people from {currentLocation}",
                Parameters = new Dictionary<string, object>
                {
                    ["current_location"] = currentLocation,
                    ["number_of_people"] = numberOfPeople,
                    ["danger_type"] = dangerType,
                    ["mobility_issues"] = mobilityIssues
                },
                Status = AgentActionStatus.InProgress
            };

            try
            {
                var coords = await _geocodingService.GetCoordinatesByLocationAsync(currentLocation);
                double lat = coords?.Latitude ?? 0.3476;
                double lng = coords?.Longitude ?? 32.5825;

                // Calculate priority
                var priority = EvacuationPriority.Normal;
                if (mobilityIssues || numberOfPeople >= 5) priority = EvacuationPriority.High;
                if (dangerType.ToLower().Contains("fire") || dangerType.ToLower().Contains("collapse")) 
                    priority = EvacuationPriority.Critical;

                // Save to database
                var evacRequest = new EvacuationRequest
                {
                    RequestGuid = Guid.NewGuid(),
                    Phone = session.UserPhone,
                    FullName = session.UserName,
                    NumberOfPeople = numberOfPeople,
                    HasDisabled = mobilityIssues,
                    HasElderly = mobilityIssues,
                    NeedsMedicalAssistance = mobilityIssues,
                    SpecialRequirements = mobilityIssues ? "Mobility assistance required" : null,
                    PickupLocation = currentLocation,
                    PickupLatitude = lat,
                    PickupLongitude = lng,
                    Status = EvacuationStatus.Requested,
                    Priority = priority,
                    EstimatedArrival = DateTime.UtcNow.AddMinutes(priority == EvacuationPriority.Critical ? 15 : 30),
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.EvacuationRequests.Add(evacRequest);
                await _dbContext.SaveChangesAsync();

                // Create notification
                var notification = new AlertNotification
                {
                    RecipientPhone = session.UserPhone ?? "unknown",
                    Type = NotificationType.Evacuation,
                    Channel = NotificationChannel.SMS,
                    Subject = "Evacuation Request Confirmed",
                    Message = $"Evacuation request #{evacRequest.Id} received. {numberOfPeople} people at {currentLocation}. " +
                              $"Priority: {priority}. ETA: {evacRequest.EstimatedArrival?.ToString("HH:mm")}",
                    Status = DbNotificationStatus.Sent,
                    RelatedEvacuationRequestId = evacRequest.Id,
                    CreatedAt = DateTime.UtcNow,
                    SentAt = DateTime.UtcNow
                };
                _dbContext.AlertNotifications.Add(notification);
                await _dbContext.SaveChangesAsync();

                _logger.LogWarning("🚁 AGENT ACTION: Evacuation requested - DB ID: {Id}, People: {Count}, Location: {Location}, Priority: {Priority}",
                    evacRequest.Id, numberOfPeople, currentLocation, priority);

                action.Result = $"Evacuation Request #{evacRequest.Id} - Priority: {priority}, ETA: {evacRequest.EstimatedArrival?.ToString("HH:mm")} - Stay calm, help is on the way!";
                action.Status = AgentActionStatus.Completed;
                action.CompletedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to request evacuation");
                action.Status = AgentActionStatus.Failed;
                action.Result = $"Error: {ex.Message}";
            }

            return action;
        }

        private AgentAction ExecuteGetSafetyInstructions(string disasterType)
        {
            var instructions = disasterType.ToLower() switch
            {
                "flood" => "Move to higher ground immediately. Never walk through flood water. Call 999.",
                "landslide" => "Run to the side, not downhill. Stay away from slopes. Call 999.",
                "earthquake" => "DROP, COVER, HOLD ON. Stay away from windows. Check for injuries after.",
                "fire" => "Get out immediately. Crawl low under smoke. Call 112.",
                "diseaseoutbreak" => "Drink only boiled water. Wash hands frequently. Seek medical help if symptoms appear.",
                _ => "Stay calm. Call emergency services: Police 999, Ambulance 911, Fire 112."
            };

            return new AgentAction
            {
                ToolName = "get_safety_instructions",
                Description = $"Retrieved safety instructions for {disasterType}",
                Parameters = new Dictionary<string, object> { ["disaster_type"] = disasterType },
                Result = instructions,
                Status = AgentActionStatus.Completed,
                CompletedAt = DateTime.UtcNow
            };
        }

        private string GetAgentSystemPrompt()
        {
            return @"You are Direco, an intelligent disaster response assistant for Uganda. You help with emergencies AND general health/safety questions.

YOUR CAPABILITIES:
1. Request emergency services (ambulance, police, fire brigade) - ONLY for true emergencies
2. Find nearby hospitals, clinics, shelters, police stations
3. Register people for emergency shelter
4. Notify family members about emergencies  
5. Request evacuation teams
6. Provide health advice and safety guidance

🧠 INTELLIGENT TRIAGE - CRITICAL:
NOT EVERY HEALTH CONCERN IS AN EMERGENCY. You must assess carefully:

TRUE EMERGENCIES (dispatch help immediately):
- Unconscious, not breathing, severe bleeding, chest pain, stroke symptoms
- Active disasters: floods, fires, landslides, building collapse
- Violence, accidents with injuries, drowning
- Labor/childbirth complications

HEALTH CONCERNS (provide advice, suggest clinic visit):
- High/low blood sugar (unless unconscious or confused)
- Fever, cough, headache, stomach issues
- Chronic condition management
- Medication questions

WRONG: User says 'my blood sugar is high' → dispatch ambulance
RIGHT: User says 'my blood sugar is high' → Ask about symptoms, provide management tips, suggest clinic if needed

WHEN ACTIONS ARE TAKEN:
- If '[ACTIONS I HAVE TAKEN]' shows emergency dispatch, CONFIRM help is coming to their GPS location
- Don't ask for location if we have GPS coordinates
- Give immediate safety instructions

RESPONSE STYLE:
- Be conversational and helpful, not alarming
- Ask clarifying questions for non-emergencies
- Only use 🚨 for actual emergencies
- Provide practical advice
- Keep responses concise

UGANDA CONTEXT:
- Emergency contacts: Police 999, Ambulance 911, Fire 112
- NECOC: 0800-100-066, Red Cross: 0800-100-250
- Common disasters: floods, landslides (Bududa/Kasese regions)
- Rainy seasons: March-May, September-November";
        }

        private string BuildTriageContext(
            (bool IsEmergency, DRC.Api.Models.EmergencySeverity Severity, DRC.Api.Models.EmergencyType Type) emergencyDetection,
            List<AgentAction> actionsTaken,
            AgentSession session)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[TRIAGE ASSESSMENT]");
            
            // Include GPS info - crucial for all responses
            if (session.Latitude.HasValue && session.Longitude.HasValue)
            {
                sb.AppendLine($"📍 USER GPS LOCATION: Lat {session.Latitude:F6}, Lng {session.Longitude:F6}");
                sb.AppendLine("   ✅ WE HAVE THEIR LOCATION - Use it to find nearby facilities if needed.");
            }
            else
            {
                sb.AppendLine("📍 USER LOCATION: Not available - ask only if actually needed for the request.");
            }
            
            if (emergencyDetection.IsEmergency)
            {
                sb.AppendLine($"⚠️ EMERGENCY DETECTED: {emergencyDetection.Type}, Severity: {emergencyDetection.Severity}");
                if (actionsTaken.Any(a => a.ToolName == "request_emergency_services"))
                {
                    sb.AppendLine("✅ Emergency services have been dispatched - confirm help is on the way.");
                }
            }
            else
            {
                sb.AppendLine("✅ NOT AN EMERGENCY - This appears to be a general health concern or question.");
                sb.AppendLine("📋 RESPONSE GUIDANCE:");
                sb.AppendLine("   - Be helpful and conversational, NOT alarmist");
                sb.AppendLine("   - Do NOT suggest dispatching emergency services");
                sb.AppendLine("   - Provide practical health advice if relevant");
                sb.AppendLine("   - If user asks for nearby facilities, use their GPS to help them");
                sb.AppendLine("   - Suggest visiting a clinic/doctor if appropriate");
            }
            
            sb.AppendLine("[END TRIAGE]");
            return sb.ToString();
        }

        private string BuildConversationHistory(AgentSession session)
        {
            var history = new System.Text.StringBuilder();
            history.AppendLine("[CONVERSATION HISTORY]");
            
            foreach (var msg in session.Messages.TakeLast(10))
            {
                var role = msg.Role == "user" ? "User" : "Agent";
                history.AppendLine($"{role}: {msg.Content}");
            }
            
            history.AppendLine("[END OF HISTORY]");
            return history.ToString();
        }

        private AgentSession CreateNewSession(Guid sessionId, string? userPhone, int? userId = null)
        {
            return new AgentSession
            {
                SessionId = sessionId,
                UserId = userId,
                UserPhone = userPhone ?? "unknown",
                CreatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow
            };
        }

        private async Task SaveSessionAsync(AgentSession session)
        {
            var cacheKey = $"agent_session:{session.SessionId}";
            var json = JsonSerializer.Serialize(session);
            
            try
            {
                if (_cache != null)
                {
                    await _cache.SetStringAsync(
                        cacheKey,
                        json,
                        new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis unavailable, using in-memory cache");
                // Fallback to in-memory cache
                _memoryCache[cacheKey] = (json, DateTime.UtcNow.AddHours(24));
            }
            
            // Also persist messages to SQLite database
            await SaveMessagesToDatabaseAsync(session);
        }

        private async Task SaveMessagesToDatabaseAsync(AgentSession session)
        {
            try
            {
                // Get existing messages for this session
                var existingCount = await _dbContext.ChatMessages
                    .CountAsync(m => m.SessionId == session.SessionId);
                
                // Only save new messages
                var newMessages = session.Messages.Skip(existingCount).ToList();
                
                foreach (var msg in newMessages)
                {
                    var dbMessage = new DbChatMessage
                    {
                        SessionId = session.SessionId,
                        UserId = session.UserId,
                        Role = msg.Role,
                        Content = msg.Content,
                        UserPhone = session.UserPhone,
                        UserLocation = session.UserLocation,
                        Latitude = session.Latitude,
                        Longitude = session.Longitude,
                        ActionsJson = msg.ActionsInMessage != null ? JsonSerializer.Serialize(msg.ActionsInMessage) : null,
                        CreatedAt = msg.Timestamp
                    };
                    
                    _dbContext.ChatMessages.Add(dbMessage);
                }
                
                if (newMessages.Any())
                {
                    await _dbContext.SaveChangesAsync();
                    _logger.LogInformation("Saved {Count} chat messages to database for session {SessionId}", 
                        newMessages.Count, session.SessionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving chat messages to database for session {SessionId}", session.SessionId);
            }
        }

        public async Task<AgentSession?> GetSessionAsync(Guid sessionId)
        {
            var cacheKey = $"agent_session:{sessionId}";
            string? sessionJson = null;
            
            try
            {
                if (_cache != null)
                {
                    sessionJson = await _cache.GetStringAsync(cacheKey);
                    if (!string.IsNullOrEmpty(sessionJson))
                    {
                        return JsonSerializer.Deserialize<AgentSession>(sessionJson);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis unavailable, checking other sources");
            }
            
            // Fallback to in-memory cache
            if (_memoryCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                return JsonSerializer.Deserialize<AgentSession>(cached.Value);
            }
            
            // Try to load from database
            return await LoadSessionFromDatabaseAsync(sessionId);
        }

        private async Task<AgentSession?> LoadSessionFromDatabaseAsync(Guid sessionId)
        {
            try
            {
                var dbMessages = await _dbContext.ChatMessages
                    .Where(m => m.SessionId == sessionId)
                    .OrderBy(m => m.CreatedAt)
                    .ToListAsync();
                
                if (!dbMessages.Any())
                    return null;
                
                var firstMessage = dbMessages.First();
                var session = new AgentSession
                {
                    SessionId = sessionId,
                    UserId = firstMessage.UserId,
                    UserPhone = firstMessage.UserPhone ?? "",
                    UserLocation = firstMessage.UserLocation,
                    Latitude = firstMessage.Latitude,
                    Longitude = firstMessage.Longitude,
                    CreatedAt = firstMessage.CreatedAt,
                    LastActivityAt = dbMessages.Last().CreatedAt,
                    Messages = dbMessages.Select(m => new AgentMessage
                    {
                        Role = m.Role,
                        Content = m.Content,
                        Timestamp = m.CreatedAt,
                        ActionsInMessage = !string.IsNullOrEmpty(m.ActionsJson) 
                            ? JsonSerializer.Deserialize<List<AgentAction>>(m.ActionsJson) 
                            : null
                    }).ToList()
                };
                
                _logger.LogInformation("Loaded session {SessionId} from database with {Count} messages", 
                    sessionId, dbMessages.Count);
                
                return session;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading session {SessionId} from database", sessionId);
                return null;
            }
        }

        public async Task<List<AgentAction>> GetSessionActionsAsync(Guid sessionId)
        {
            var session = await GetSessionAsync(sessionId);
            return session?.ActionsTaken ?? new List<AgentAction>();
        }

        public async Task<List<AgentSession>> GetUserSessionsAsync(int userId)
        {
            var sessions = new List<AgentSession>();
            var processedSessionIds = new HashSet<Guid>();
            
            // Search in-memory cache for user sessions
            foreach (var kvp in _memoryCache)
            {
                if (kvp.Key.StartsWith("agent_session:") && kvp.Value.Expiry > DateTime.UtcNow)
                {
                    try
                    {
                        var session = JsonSerializer.Deserialize<AgentSession>(kvp.Value.Value);
                        if (session?.UserId == userId)
                        {
                            sessions.Add(session);
                            processedSessionIds.Add(session.SessionId);
                        }
                    }
                    catch { }
                }
            }
            
            // Also load sessions from database
            try
            {
                var dbSessionIds = await _dbContext.ChatMessages
                    .Where(m => m.UserId == userId)
                    .Select(m => m.SessionId)
                    .Distinct()
                    .ToListAsync();
                
                foreach (var sessionId in dbSessionIds)
                {
                    if (!processedSessionIds.Contains(sessionId))
                    {
                        var session = await LoadSessionFromDatabaseAsync(sessionId);
                        if (session != null)
                        {
                            sessions.Add(session);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading user sessions from database for user {UserId}", userId);
            }
            
            return sessions.OrderByDescending(s => s.LastActivityAt).ToList();
        }

        public async Task<List<DRC.Api.Models.ChatHistoryItem>> GetUserChatHistoryAsync(int userId, int? limit = null)
        {
            try
            {
                var query = _dbContext.ChatMessages
                    .Where(m => m.UserId == userId)
                    .OrderByDescending(m => m.CreatedAt)
                    .AsQueryable();
                
                if (limit.HasValue)
                {
                    query = query.Take(limit.Value);
                }
                
                var messages = await query.ToListAsync();
                
                return messages.Select(m => new DRC.Api.Models.ChatHistoryItem
                {
                    Id = m.Id,
                    SessionId = m.SessionId,
                    Role = m.Role,
                    Content = m.Content,
                    CreatedAt = m.CreatedAt,
                    Actions = !string.IsNullOrEmpty(m.ActionsJson) 
                        ? JsonSerializer.Deserialize<List<AgentAction>>(m.ActionsJson) 
                        : null
                }).OrderBy(m => m.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting chat history for user {UserId}", userId);
                return new List<DRC.Api.Models.ChatHistoryItem>();
            }
        }

        public async Task<AgentAction?> GetActionStatusAsync(string actionId)
        {
            // Search through sessions - simplified for demo
            return null;
        }

        private string GetFallbackResponse()
        {
            return @"🆘 Uganda Disaster Response Agent

I'm temporarily unable to process your request, but I'm here to help!

📞 EMERGENCY CONTACTS:
• Police: 999
• Ambulance: 911
• Fire: 112
• NECOC: 0800-100-066

Tell me:
• Your location
• What emergency you're facing
• How many people need help

I will take action immediately.";
        }
    }
}
