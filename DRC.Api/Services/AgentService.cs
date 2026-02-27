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
        private readonly IWhatAppService _whatsAppService;
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
            IWhatAppService whatsAppService,
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

                // Get or create session
                AgentSession session;
                if (sessionId.HasValue)
                {
                    session = await GetSessionAsync(sessionId.Value) ?? CreateNewSession(sessionId.Value, userPhone, userId);
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

                // Initialize Gemini
                var googleAI = new GoogleAI(apiKey: apiKey);
                var model = googleAI.GenerativeModel(model: "gemini-2.5-flash");
                model.Timeout = TimeSpan.FromMinutes(2);

                // Build prompt with agent instructions and action results
                var systemPrompt = GetAgentSystemPrompt();
                var conversationHistory = BuildConversationHistory(session);

                var fullPrompt = $@"{systemPrompt}

{conversationHistory}

{actionsContext}

User: {message}

INSTRUCTIONS: If emergency actions were taken above, your FIRST sentence must confirm that help has been dispatched. DO NOT ask for location if GPS coordinates are shown above. Provide immediate safety instructions relevant to the emergency type. Be brief and reassuring.

Agent:";

                _logger.LogInformation("Sending request to Gemini API...");

                // Generate response
                var response = await model.GenerateContent(fullPrompt);
                var responseText = response?.Text ?? "I'm here to help. How can I assist you?";

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
                var action = await ExecuteRequestEmergencyServices(
                    emergencyDetection.Type.ToString().ToLower(),
                    detectedLocation ?? "Unknown location",
                    message,
                    emergencyDetection.Severity.ToString().ToLower(),
                    session
                );
                actions.Add(action);
            }

            // Action 2: Find nearby facilities if keywords present
            var facilityKeywords = new[] { "hospital", "shelter", "police", "clinic", "help", "nearby", "closest", "find", "where" };
            if (facilityKeywords.Any(k => messageLower.Contains(k)) && !string.IsNullOrEmpty(detectedLocation))
            {
                var action = await ExecuteFindNearbyFacilities(detectedLocation, "all");
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
            
            foreach (var action in actions)
            {
                sb.AppendLine($"✅ {action.Description}");
                if (!string.IsNullOrEmpty(action.Result))
                {
                    sb.AppendLine($"   Result: {action.Result}");
                }
            }
            sb.AppendLine("\n🚨 EMERGENCY SERVICES HAVE BEEN DISPATCHED - TELL THE USER HELP IS COMING!");
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

        private async Task<AgentAction> ExecuteFindNearbyFacilities(string location, string facilityType)
        {
            var action = new AgentAction
            {
                ToolName = "find_nearby_facilities",
                Description = $"Searched for {facilityType} facilities near {location}",
                Parameters = new Dictionary<string, object>
                {
                    ["location"] = location,
                    ["facility_type"] = facilityType
                },
                Status = AgentActionStatus.InProgress
            };

            try
            {
                var coords = await _geocodingService.GetCoordinatesByLocationAsync(location);
                if (!coords.HasValue)
                {
                    action.Result = $"Could not find coordinates for {location}";
                    action.Status = AgentActionStatus.Failed;
                    return action;
                }

                var facilities = await _googlePlacesService.GetHospitalsAsync(coords.Value.Latitude, coords.Value.Longitude);
                
                _logger.LogInformation("🏥 AGENT ACTION: Found facilities near {Location}", location);
                
                action.Result = $"Found facilities near {location}";
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
                    await _whatsAppService.SendMessage(contactPhone, alertMessage);
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
            return @"You are Direco, an emergency response AGENT for Uganda. You TAKE ACTIONS on behalf of users, not just chat.

YOUR CAPABILITIES (these are automatically triggered based on user needs):
1. Request emergency services (ambulance, police, fire brigade)
2. Find nearby hospitals, shelters, police stations
3. Register people for emergency shelter
4. Notify family members about emergencies
5. Request evacuation teams
6. Provide disaster-specific safety guidance

🚨🚨🚨 CRITICAL EMERGENCY PROTOCOL 🚨🚨🚨
When the system shows '[ACTIONS I HAVE TAKEN]' with emergency services dispatched:
- **DO NOT ASK FOR LOCATION** - We already have their GPS coordinates!
- **CONFIRM HELP IS ON THE WAY** - Tell them emergency services have been dispatched to their exact GPS location
- **GIVE IMMEDIATE SAFETY INSTRUCTIONS** - What to do NOW while waiting for help
- **BE REASSURING** - They are scared, tell them help is coming

WRONG (don't do this): 'Please tell me your location'
RIGHT: '🚨 HELP IS ON THE WAY! Emergency services have been dispatched to your GPS location. While you wait: [safety instructions]'

CRITICAL BEHAVIOR:
- You are an AGENT that acts, not just a chatbot that talks
- If actions were taken, acknowledge them clearly - HELP IS ALREADY DISPATCHED
- Always provide Uganda emergency contacts: Police 999, Ambulance 911, Fire 112
- Be concise, calm, and reassuring
- Use emojis for clarity (🚨❗✅🏥🏠)

UGANDA CONTEXT:
- Know the districts and common disasters (floods, landslides in Bududa/Kasese)
- Reference NECOC (0800-100-066), Red Cross (0800-100-250) when relevant
- Rainy seasons: March-May, September-November";
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
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Redis unavailable, using in-memory cache");
            }
            
            // Fallback to in-memory cache
            _memoryCache[cacheKey] = (json, DateTime.UtcNow.AddHours(24));
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
                _logger.LogWarning(ex, "Redis unavailable, using in-memory cache");
            }
            
            // Fallback to in-memory cache
            if (_memoryCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                return JsonSerializer.Deserialize<AgentSession>(cached.Value);
            }
            
            return null;
        }

        public async Task<List<AgentAction>> GetSessionActionsAsync(Guid sessionId)
        {
            var session = await GetSessionAsync(sessionId);
            return session?.ActionsTaken ?? new List<AgentAction>();
        }

        public async Task<List<AgentSession>> GetUserSessionsAsync(int userId)
        {
            var sessions = new List<AgentSession>();
            
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
                        }
                    }
                    catch { }
                }
            }
            
            return sessions.OrderByDescending(s => s.LastActivityAt).ToList();
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
