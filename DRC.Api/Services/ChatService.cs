using Mscc.GenerativeAI;
using DRC.Api.Interfaces;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using DRC.Api.Models;

namespace DRC.Api.Services
{
    public class ChatService : IChatService
    {
        private readonly IConfiguration _configuration;
        private readonly ICepService _cepService;
        private readonly IChatCacheService _chatCacheService;
        private readonly IS2iDService _s2iDService;
        private readonly IDistributedCache _cache;
        private readonly IGooglePlacesService _googlePlacesService;
        private readonly IGeocodingService _geocodingService;
        private readonly IBenfeitoriaService _benfeitoriaService;
        private readonly IEmergencyAlertService _emergencyAlertService;
        private readonly ILogger<ChatService> _logger;

        public ChatService(
            IConfiguration configuration, 
            IBenfeitoriaService benfeitoriaService, 
            ICepService cepService, 
            IChatCacheService chatCacheService, 
            IS2iDService s2iDService, 
            IDistributedCache cache, 
            IGooglePlacesService googlePlacesService, 
            IGeocodingService geocodingService,
            IEmergencyAlertService emergencyAlertService,
            ILogger<ChatService> logger)
        {
            _configuration = configuration;
            _cepService = cepService;
            _chatCacheService = chatCacheService;
            _googlePlacesService = googlePlacesService;
            _s2iDService = s2iDService;
            _cache = cache;
            _geocodingService = geocodingService;
            _benfeitoriaService = benfeitoriaService;
            _emergencyAlertService = emergencyAlertService;
            _logger = logger;
        }

        private async Task<string> GetDonationPlaces(string palavra_chave)
        {
            try
            {
                return await _benfeitoriaService.GetProjectsByKeywordAsync(palavra_chave);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting donation places for keyword: {Keyword}", palavra_chave);
                return "Unable to search for donation locations at this time.";
            }
        }

        private async Task<string> GetLocationInfoAsync(string location)
        {
            try
            {
                var locationName = await _geocodingService.GetLocationNameAsync(location);
                return locationName ?? $"Location: {location}, Uganda";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting location info for: {Location}", location);
                return $"Location: {location}, Uganda";
            }
        }

        private async Task<string> GetAvailableSheltersAsync(string location)
        {
            try
            {
                var coords = await _geocodingService.GetCoordinatesByLocationAsync(location);
                if (coords == null)
                {
                    return "Unable to find coordinates for this location.";
                }
                var result = await _googlePlacesService.GetHospitalsAsync(coords.Value.Latitude, coords.Value.Longitude);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting shelters for location: {Location}", location);
                return "Unable to search for shelters at this time.";
            }
        }

        private async Task<Dictionary<string, Cobrade>> GetCobradesAsync()
        {
            var cacheKey = "cobrades2";
            var cobradesJson = await _cache.GetStringAsync(cacheKey);
            if (string.IsNullOrEmpty(cobradesJson))
            {
                var cobrades = await _s2iDService.GetCobradesAsync();
                var cobradesDict = cobrades.ToDictionary(c => c.CobradeId.ToString());
                cobradesJson = JsonSerializer.Serialize(cobradesDict);
                await _cache.SetStringAsync(cacheKey, cobradesJson, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                });
            }

            return JsonSerializer.Deserialize<Dictionary<string, Cobrade>>(cobradesJson) ?? new Dictionary<string, Cobrade>();
        }

        private async Task<string> GetDisastersAsync(string location)
        {
            try
            {
                // For Uganda, we provide common disaster types in the region
                // In production, this would connect to Uganda's disaster management systems
                var commonDisasters = new[]
                {
                    "Floods - common during rainy seasons (March-May, September-November)",
                    "Landslides - especially in mountainous regions like Bududa, Kasese",
                    "Drought - affects northern and eastern regions",
                    "Disease outbreaks - malaria, cholera during flood seasons",
                    "Earthquakes - western rift valley region"
                };
                return $"Common disasters in Uganda: {string.Join("; ", commonDisasters)}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting disasters for location: {Location}", location);
                return "No current disaster alerts for this area.";
            }
        }

        private string GetSystemPrompt()
        {
            return @"You are an emergency assistance specialist named Direco, helping people in Uganda during disasters and emergencies. Your task is to identify and respond to user needs in a friendly, calm, and attentive manner.

IMPORTANT UGANDA EMERGENCY CONTACTS:
- Police Emergency: 999 or 112
- Ambulance/Medical: 911
- Fire Brigade: 112
- Uganda Red Cross: +256 414 258 701
- Office of the Prime Minister (Disaster Preparedness): +256 414 259 439
- National Emergency Coordination & Operations Centre (NECOC): +256 417 774 500

KEY UGANDA DISASTER AGENCIES:
- Office of the Prime Minister (OPM) - Disaster Preparedness and Management
- Uganda Red Cross Society (URCS)
- National Emergency Coordination & Operations Centre (NECOC)
- Uganda People's Defence Forces (UPDF) - for major disasters
- District Disaster Management Committees

COMMON DISASTERS IN UGANDA:
- Floods (especially during rainy seasons: March-May, September-November)
- Landslides (mountainous regions: Bududa, Kasese, Bundibugyo)
- Drought (Northern and Eastern regions)
- Disease outbreaks (cholera, malaria during floods)
- Earthquakes (Western Rift Valley region)

RULES:
1. Always respond in English
2. Be empathetic, calm, and reassuring
3. Offer clear options: shelters, hospitals, police stations, disaster information
4. Ask for the district or location name when needed to locate nearby services
5. Use the context information provided to give accurate responses
6. Format responses clearly with bullet points for locations and contacts
7. In life-threatening emergencies, ALWAYS provide the emergency numbers first
8. Mention relevant NGOs and relief organizations when appropriate";
        }

        public async Task<(string? guid, string message)> SendMessage(Guid? guid, string message)
        {
            try
            {
                var apiKey = _configuration["Apps:Gemini:Key"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogError("Gemini API key not configured");
                    return (guid?.ToString(), "Error: API key not configured. Please configure the Gemini API key.");
                }

                // Get or create conversation history
                List<ChatMessage> history;
                if (guid.HasValue)
                {
                    history = await _chatCacheService.GetConversationAsync(guid.Value) ?? new List<ChatMessage>();
                }
                else
                {
                    guid = Guid.NewGuid();
                    history = new List<ChatMessage>();
                }

                // Check for Uganda location mentions and gather context
                string contextInfo = "";
                
                // List of major Uganda districts and common location identifiers
                var ugandaDistricts = new[] {
                    "kampala", "wakiso", "mukono", "jinja", "entebbe", "mbarara", "gulu", "lira", 
                    "mbale", "soroti", "arua", "fort portal", "kasese", "kabale", "masaka", "hoima",
                    "bududa", "bundibugyo", "kapchorwa", "moroto", "kotido", "karamoja", "teso",
                    "busoga", "buganda", "ankole", "acholi", "lango", "west nile", "tooro", "bunyoro"
                };
                
                // Check if message contains a Uganda location
                var messageLower = message.ToLower();
                var detectedLocation = ugandaDistricts.FirstOrDefault(d => messageLower.Contains(d));
                
                // Also check for location patterns like "in [location]" or "near [location]"
                if (detectedLocation == null)
                {
                    var locationPatterns = new[] { 
                        @"in\s+([A-Za-z\s]+?)(?:\s|$|,|\.|\?|!)",
                        @"near\s+([A-Za-z\s]+?)(?:\s|$|,|\.|\?|!)",
                        @"at\s+([A-Za-z\s]+?)(?:\s|$|,|\.|\?|!)",
                        @"around\s+([A-Za-z\s]+?)(?:\s|$|,|\.|\?|!)"
                    };
                    
                    foreach (var pattern in locationPatterns)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(message, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success && match.Groups[1].Value.Length > 2 && match.Groups[1].Value.Length < 30)
                        {
                            var potentialLocation = match.Groups[1].Value.Trim();
                            // Verify it's not a common word
                            var commonWords = new[] { "help", "need", "the", "emergency", "hospital", "shelter", "danger" };
                            if (!commonWords.Contains(potentialLocation.ToLower()))
                            {
                                detectedLocation = potentialLocation;
                                break;
                            }
                        }
                    }
                }
                
                if (!string.IsNullOrEmpty(detectedLocation))
                {
                    _logger.LogInformation("Location detected in message: {Location}", detectedLocation);
                    
                    // Gather information in parallel
                    var locationTask = GetLocationInfoAsync(detectedLocation);
                    var disastersTask = GetDisastersAsync(detectedLocation);
                    var sheltersTask = GetAvailableSheltersAsync(detectedLocation);
                    
                    await Task.WhenAll(locationTask, disastersTask, sheltersTask);
                    
                    contextInfo = $@"

[CONTEXT FOR LOCATION: {detectedLocation.ToUpper()}, UGANDA]
Location Info: {await locationTask}
Disaster Information: {await disastersTask}
Nearby Facilities (hospitals, shelters, police): {await sheltersTask}
[END OF CONTEXT]

";
                }

                // Check for donation/help keyword search
                var donationKeywords = new[] { "donate", "donation", "help", "contribute", "volunteer", "relief", "aid", "support" };
                if (donationKeywords.Any(k => messageLower.Contains(k)))
                {
                    var donationPlaces = await GetDonationPlaces("emergency relief Uganda");
                    contextInfo += $@"

[DONATION AND RELIEF ORGANIZATIONS]
Key organizations accepting donations for Uganda disaster relief:
- Uganda Red Cross Society: https://www.redcrossug.org - Accepts donations and volunteers
- UNHCR Uganda: For refugee assistance
- World Food Programme Uganda: Food aid programs
- Save the Children Uganda: Child-focused emergency response
- Local churches and mosques often coordinate community relief
{donationPlaces}
[END OF DONATION INFO]

";
                }

                // 🚨 CRITICAL: Detect emergency situations and alert service providers
                var phoneNumber = guid?.ToString() ?? "unknown"; // In WhatsApp context, this would be the actual phone number
                var emergencyDetection = await _emergencyAlertService.DetectEmergencyAsync(message);
                
                if (emergencyDetection.IsEmergency)
                {
                    _logger.LogWarning("🚨 EMERGENCY DETECTED: {Type} - Severity: {Severity} - Location: {Location}",
                        emergencyDetection.Type, emergencyDetection.Severity, detectedLocation ?? "Unknown");
                    
                    // Get coordinates if we have a location
                    double latitude = 0.3476, longitude = 32.5825; // Default to Kampala
                    if (!string.IsNullOrEmpty(detectedLocation))
                    {
                        var coords = await _geocodingService.GetCoordinatesByLocationAsync(detectedLocation);
                        if (coords.HasValue)
                        {
                            latitude = coords.Value.Latitude;
                            longitude = coords.Value.Longitude;
                        }
                    }
                    
                    // Create the emergency alert
                    var alert = await _emergencyAlertService.CreateAlertAsync(
                        emergencyDetection.Type,
                        emergencyDetection.Severity,
                        detectedLocation ?? "Unknown - Uganda",
                        latitude,
                        longitude,
                        message,
                        phoneNumber,
                        $"WhatsApp User ({phoneNumber})"
                    );
                    
                    _logger.LogInformation("📢 Alert created: {AlertId}", alert.Id);
                    
                    // For Critical and High severity, immediately notify service providers
                    if (emergencyDetection.Severity == EmergencySeverity.Critical || emergencyDetection.Severity == EmergencySeverity.High)
                    {
                        await _emergencyAlertService.NotifyServiceProvidersAsync(alert);
                        _logger.LogWarning("🚑 Service providers notified for alert: {AlertId}", alert.Id);
                        
                        // Add alert notification info to context so AI knows to reassure the user
                        contextInfo += $@"

[🚨 AUTOMATIC EMERGENCY ALERT TRIGGERED]
Alert ID: {alert.Id}
Severity: {emergencyDetection.Severity}
Type: {emergencyDetection.Type}
Emergency services have been automatically notified including:
- NECOC (National Emergency Coordination Centre)
- Uganda Police (999)
- Uganda Red Cross
- Nearest hospital emergency department
The user should be reassured that help is on the way and given immediate safety instructions.
[END OF ALERT INFO]

";
                    }
                }

                // Create the Gemini client
                var googleAI = new GoogleAI(apiKey: apiKey);
                var model = googleAI.GenerativeModel(model: "gemini-2.5-flash-lite");
                model.Timeout = TimeSpan.FromSeconds(25);

                // Build the conversation prompt
                var conversationBuilder = new System.Text.StringBuilder();
                conversationBuilder.AppendLine(GetSystemPrompt());
                conversationBuilder.AppendLine();
                
                // Add history
                foreach (var msg in history)
                {
                    var role = msg.Role == "user" ? "User" : "Direco";
                    conversationBuilder.AppendLine($"{role}: {msg.Content}");
                }
                
                // Add current message with context
                var userMessage = !string.IsNullOrEmpty(contextInfo) 
                    ? $"{message}\n{contextInfo}" 
                    : message;
                conversationBuilder.AppendLine($"User: {userMessage}");
                conversationBuilder.AppendLine("Direco:");

                _logger.LogInformation("Sending request to Gemini API...");

                // Generate response with better error handling
                string responseText;
                try
                {
                    var response = await model.GenerateContent(conversationBuilder.ToString());
                    responseText = response?.Text ?? "I'm here to help. What emergency are you facing?";
                }
                catch (HttpRequestException httpEx) when (httpEx.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("Gemini API rate limited (429)");
                    return (guid?.ToString(), "The service is busy. For emergencies call: Police 999, Ambulance 911, Fire 112");
                }
                catch (HttpRequestException httpEx) when (httpEx.Message.Contains("429"))
                {
                    _logger.LogWarning("Gemini API rate limited");
                    return (guid?.ToString(), "The service is busy. For emergencies call: Police 999, Ambulance 911, Fire 112");
                }
                catch (HttpRequestException httpEx)
                {
                    _logger.LogError(httpEx, "HTTP error calling Gemini API. Status: {StatusCode}", httpEx.StatusCode);
                    return (guid?.ToString(), "I'm having trouble connecting. For emergencies call: Police 999, Ambulance 911");
                }
                catch (TaskCanceledException)
                {
                    _logger.LogWarning("Gemini API timeout");
                    return (guid?.ToString(), "Response timed out. For emergencies call: Police 999, Ambulance 911");
                }

                _logger.LogInformation("Received response from Gemini API");

                // Save to history
                history.Add(new ChatMessage { Role = "user", Content = message });
                history.Add(new ChatMessage { Role = "model", Content = responseText });
                
                await _chatCacheService.SaveConversationAsync(guid.Value, history);

                return (guid.ToString(), responseText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to Gemini");
                
                // Provide helpful fallback response with emergency contacts
                var fallbackResponse = @"🆘 *Uganda Disaster Response*

I'm temporarily unable to process your message, but here's how to get help:

📞 *EMERGENCY CONTACTS:*
• Police: *999*
• Ambulance: *911*
• Fire: *112*
• NECOC: *0800 100 066* (toll-free)
• Red Cross: *0800 100 250*

⚠️ *IF THIS IS AN EMERGENCY:*
1. Call 999 immediately
2. Move to a safe location
3. Help others if safe to do so

💬 Please try sending your message again in a few minutes.

Type *HELP* for the menu or *SOS* for emergency contacts.";
                
                return (guid?.ToString(), fallbackResponse);
            }
        }
    }

    public class ChatMessage
    {
        public string Role { get; set; } = "user";
        public string Content { get; set; } = "";
    }
}
