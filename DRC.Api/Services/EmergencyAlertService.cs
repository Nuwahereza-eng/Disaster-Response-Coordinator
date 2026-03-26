using DRC.Api.Interfaces;
using DRC.Api.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DRC.Api.Services
{
    public class EmergencyAlertService : IEmergencyAlertService
    {
        private readonly IDistributedCache _cache;
        private readonly IServiceProvider _serviceProvider; // Lazy resolution to break circular dependency
        private readonly ILogger<EmergencyAlertService> _logger;
        private readonly IConfiguration _configuration;

        // Pre-configured service providers for Uganda
        private static readonly List<Models.ServiceProvider> _serviceProviders = new()
        {
            // National Emergency Services
            new Models.ServiceProvider
            {
                Id = "necoc-national",
                Name = "NECOC - National Emergency",
                Type = ServiceProviderType.NECOC,
                Phone = "0800100066",
                WhatsAppNumber = "256417774500",
                CoveredDistricts = new List<string> { "*" }, // All districts
                HandledEmergencies = Enum.GetValues<EmergencyType>().ToList()
            },
            new Models.ServiceProvider
            {
                Id = "redcross-national",
                Name = "Uganda Red Cross Society",
                Type = ServiceProviderType.RedCross,
                Phone = "0800100250",
                WhatsAppNumber = "256414258701",
                CoveredDistricts = new List<string> { "*" },
                HandledEmergencies = new List<EmergencyType> 
                { 
                    EmergencyType.Landslide, EmergencyType.Flood, EmergencyType.Earthquake,
                    EmergencyType.Fire, EmergencyType.MedicalEmergency, EmergencyType.Accident
                }
            },
            new Models.ServiceProvider
            {
                Id = "police-999",
                Name = "Uganda Police Emergency",
                Type = ServiceProviderType.Police,
                Phone = "999",
                WhatsAppNumber = "",
                CoveredDistricts = new List<string> { "*" },
                HandledEmergencies = new List<EmergencyType>
                {
                    EmergencyType.Violence, EmergencyType.Accident, EmergencyType.MissingPerson,
                    EmergencyType.Fire, EmergencyType.Landslide, EmergencyType.Flood
                }
            },
            new Models.ServiceProvider
            {
                Id = "ambulance-911",
                Name = "National Ambulance Service",
                Type = ServiceProviderType.Ambulance,
                Phone = "911",
                WhatsAppNumber = "",
                CoveredDistricts = new List<string> { "*" },
                HandledEmergencies = new List<EmergencyType>
                {
                    EmergencyType.MedicalEmergency, EmergencyType.Accident, EmergencyType.Drowning,
                    EmergencyType.Landslide, EmergencyType.BuildingCollapse
                }
            },
            // Regional providers - Bududa (Landslide prone)
            new Models.ServiceProvider
            {
                Id = "bududa-ddc",
                Name = "Bududa District Disaster Committee",
                Type = ServiceProviderType.DistrictDisasterCommittee,
                Phone = "0772123456", // Example
                WhatsAppNumber = "256772123456",
                CoveredDistricts = new List<string> { "bududa", "manafwa", "sironko" },
                HandledEmergencies = new List<EmergencyType> { EmergencyType.Landslide, EmergencyType.Flood }
            },
            new Models.ServiceProvider
            {
                Id = "mbale-hospital",
                Name = "Mbale Regional Referral Hospital",
                Type = ServiceProviderType.Hospital,
                Phone = "0454435678",
                WhatsAppNumber = "256454435678",
                CoveredDistricts = new List<string> { "mbale", "bududa", "sironko", "manafwa", "bulambuli" },
                HandledEmergencies = new List<EmergencyType>
                {
                    EmergencyType.MedicalEmergency, EmergencyType.Landslide, EmergencyType.Accident
                }
            },
            // Kasese (Flood & Landslide prone)
            new Models.ServiceProvider
            {
                Id = "kasese-ddc",
                Name = "Kasese District Disaster Committee",
                Type = ServiceProviderType.DistrictDisasterCommittee,
                Phone = "0772234567",
                WhatsAppNumber = "256772234567",
                CoveredDistricts = new List<string> { "kasese", "bundibugyo" },
                HandledEmergencies = new List<EmergencyType> { EmergencyType.Landslide, EmergencyType.Flood, EmergencyType.Earthquake }
            },
            // Kampala
            new Models.ServiceProvider
            {
                Id = "mulago-hospital",
                Name = "Mulago National Referral Hospital",
                Type = ServiceProviderType.Hospital,
                Phone = "0414541188",
                WhatsAppNumber = "256414541188",
                CoveredDistricts = new List<string> { "kampala", "wakiso", "mukono" },
                HandledEmergencies = Enum.GetValues<EmergencyType>().ToList()
            }
        };

        // Emergency keywords for detection
        private static readonly Dictionary<EmergencyType, string[]> _emergencyKeywords = new()
        {
            [EmergencyType.Landslide] = new[] { "landslide", "mudslide", "slope collapse", "hill collapse", "ground sliding", "soil collapse", "mountain collapse" },
            [EmergencyType.Flood] = new[] { "flood", "flooding", "water rising", "drowning", "submerged", "water everywhere", "river overflow" },
            [EmergencyType.Fire] = new[] { "fire", "burning", "flames", "smoke", "house on fire", "forest fire", "wildfire", "fuel truck", "fuel tanker", "petrol", "diesel spill", "gas leak", "leaking fuel", "fuel spill", "tanker accident", "fuel leaking" },
            [EmergencyType.Earthquake] = new[] { "earthquake", "tremor", "ground shaking", "earth shaking", "quake" },
            [EmergencyType.DiseaseOutbreak] = new[] { "cholera", "outbreak", "epidemic", "many sick", "disease spreading", "ebola", "plague" },
            [EmergencyType.Accident] = new[] { "accident", "crash", "collision", "vehicle accident", "car crash", "boda accident", "road accident", "truck off road", "off the road", "overturned", "rolled over", "truck accident", "lorry accident", "tanker", "fuel truck", "truck overturned", "vehicle overturned", "matatu accident", "bus accident", "trailer accident" },
            [EmergencyType.Violence] = new[] { "attack", "fighting", "violence", "shooting", "armed", "robbery", "assault" },
            [EmergencyType.MedicalEmergency] = new[] { "heart attack", "stroke", "severe bleeding", "unconscious", "not breathing", "giving birth", "labor", "seizure", "convulsions", "choking", "poisoning", "overdose", "chest pain", "can't breathe", "allergic reaction", "anaphylaxis" },
            [EmergencyType.Drowning] = new[] { "drowning", "fell in water", "can't swim", "in the river", "in the lake" },
            [EmergencyType.BuildingCollapse] = new[] { "building collapse", "house collapsed", "roof fell", "structure collapse", "wall fell", "trapped inside", "trapped in building", "trapped in the building", "building hit", "missile", "explosion", "bombed" },
            [EmergencyType.MissingPerson] = new[] { "missing", "lost child", "can't find", "disappeared", "lost person" }
        };

        // Non-emergency medical keywords - require assessment, not immediate dispatch
        private static readonly string[] _medicalConcernKeywords = { "blood sugar", "glucose", "diabetes", "blood pressure", "headache", "fever", "cough", "stomach pain", "diarrhea", "vomiting", "feeling sick", "not feeling well", "medicine", "medication", "clinic", "doctor" };

        // Severity keywords
        private static readonly string[] _criticalKeywords = { "dying", "dead", "death", "buried", "trapped", "can't breathe", "severe bleeding", "unconscious", "many people", "children", "emergency", "help now", "urgent", "critical", "life threatening", "sos", "999", "please help", "fuel truck", "fuel tanker", "petrol tanker", "diesel tanker", "tanker accident", "fuel spill", "gas leak" };
        private static readonly string[] _highKeywords = { "injured", "hurt", "danger", "serious", "need help", "stuck", "stranded", "bad", "worse", "accident", "crash", "overturned", "off road", "off the road", "truck", "lorry", "leaking", "spill" };

        public EmergencyAlertService(
            IDistributedCache cache,
            IServiceProvider serviceProvider,
            ILogger<EmergencyAlertService> logger,
            IConfiguration configuration)
        {
            _cache = cache;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<(bool IsEmergency, EmergencySeverity Severity, EmergencyType Type)> DetectEmergencyAsync(string message)
        {
            var lowerMessage = message.ToLower();
            
            // First check if this is a medical concern (not an emergency)
            bool isMedicalConcern = _medicalConcernKeywords.Any(k => lowerMessage.Contains(k));
            bool hasCriticalSymptoms = _criticalKeywords.Any(k => lowerMessage.Contains(k));
            
            // Medical concerns without critical symptoms are NOT emergencies
            if (isMedicalConcern && !hasCriticalSymptoms)
            {
                _logger.LogInformation("Medical concern detected (not emergency): {Message}", message);
                return (false, EmergencySeverity.Low, EmergencyType.Unknown);
            }
            
            // Detect emergency type
            var detectedType = EmergencyType.Unknown;
            int maxMatches = 0;

            foreach (var (type, keywords) in _emergencyKeywords)
            {
                int matches = keywords.Count(k => lowerMessage.Contains(k));
                if (matches > maxMatches)
                {
                    maxMatches = matches;
                    detectedType = type;
                }
            }

            if (detectedType == EmergencyType.Unknown && maxMatches == 0)
            {
                return (false, EmergencySeverity.Low, EmergencyType.Unknown);
            }

            // Detect severity
            var severity = EmergencySeverity.Medium;
            
            if (hasCriticalSymptoms)
            {
                severity = EmergencySeverity.Critical;
            }
            else if (_highKeywords.Any(k => lowerMessage.Contains(k)))
            {
                severity = EmergencySeverity.High;
            }

            // Certain emergency types are always high severity minimum
            if (detectedType is EmergencyType.Landslide or EmergencyType.Earthquake or EmergencyType.BuildingCollapse)
            {
                severity = severity < EmergencySeverity.High ? EmergencySeverity.High : severity;
            }

            _logger.LogInformation("Emergency detected: Type={Type}, Severity={Severity}", detectedType, severity);
            
            return (true, severity, detectedType);
        }

        public async Task<EmergencyAlert> CreateAlertAsync(
            string victimPhone,
            string message,
            string location,
            double? latitude,
            double? longitude,
            EmergencySeverity severity,
            EmergencyType type)
        {
            var alert = new EmergencyAlert
            {
                VictimPhone = victimPhone,
                VictimMessage = message,
                Location = location,
                Latitude = latitude,
                Longitude = longitude,
                Severity = severity,
                Type = type,
                Description = $"{type} emergency reported in {location}. Severity: {severity}",
                EstimatedPeopleAffected = EstimatePeopleAffected(message)
            };

            // Store in cache
            await _cache.SetStringAsync(
                $"alert:{alert.Id}",
                JsonSerializer.Serialize(alert),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                });

            // Add to active alerts list
            var activeAlerts = await GetActiveAlertsAsync();
            activeAlerts.Add(alert);
            await _cache.SetStringAsync(
                "alerts:active",
                JsonSerializer.Serialize(activeAlerts.Select(a => a.Id).ToList()),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                });

            _logger.LogWarning("🚨 EMERGENCY ALERT CREATED: {AlertId} - {Type} in {Location} - Severity: {Severity}",
                alert.Id, type, location, severity);

            return alert;
        }

        public async Task NotifyServiceProvidersAsync(EmergencyAlert alert)
        {
            var providers = await GetRelevantProvidersAsync(alert.Location, alert.Type);
            
            _logger.LogInformation("Notifying {Count} service providers for alert {AlertId}", providers.Count, alert.Id);

            // Lazily resolve WhatsApp service to avoid circular dependency
            var whatsAppService = _serviceProvider.GetService<IWhatAppService>();

            foreach (var provider in providers)
            {
                try
                {
                    var notificationMessage = FormatAlertNotification(alert, provider);
                    
                    // Send via WhatsApp if available
                    if (!string.IsNullOrEmpty(provider.WhatsAppNumber) && whatsAppService != null)
                    {
                        await whatsAppService.SendMessage(provider.WhatsAppNumber, notificationMessage);
                        _logger.LogInformation("✅ Alert sent to {Provider} via WhatsApp", provider.Name);
                    }
                    else
                    {
                        _logger.LogWarning("📱 WhatsApp not configured for {Provider} - Phone: {Phone}", provider.Name, provider.Phone);
                    }
                    
                    alert.NotifiedProviders.Add(provider.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to notify provider {Provider}", provider.Name);
                }
            }

            // Update the alert in cache
            await _cache.SetStringAsync(
                $"alert:{alert.Id}",
                JsonSerializer.Serialize(alert),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                });
        }

        public async Task<List<Models.ServiceProvider>> GetRelevantProvidersAsync(string location, EmergencyType type)
        {
            var locationLower = location.ToLower();
            
            return _serviceProviders
                .Where(p => p.IsActive)
                .Where(p => p.HandledEmergencies.Contains(type) || p.HandledEmergencies.Contains(EmergencyType.Unknown))
                .Where(p => p.CoveredDistricts.Contains("*") || p.CoveredDistricts.Any(d => locationLower.Contains(d)))
                .OrderByDescending(p => p.Type == ServiceProviderType.NECOC) // NECOC first
                .ThenByDescending(p => p.Type == ServiceProviderType.DistrictDisasterCommittee) // Local DDC second
                .ThenByDescending(p => p.CoveredDistricts.Any(d => locationLower.Contains(d))) // Local providers
                .ToList();
        }

        public async Task UpdateAlertStatusAsync(string alertId, AlertStatus status, string providerName, string message)
        {
            var alert = await GetAlertAsync(alertId);
            if (alert == null) return;

            alert.Status = status;
            alert.Updates.Add(new AlertUpdate
            {
                Provider = providerName,
                Message = message,
                NewStatus = status
            });

            await _cache.SetStringAsync(
                $"alert:{alert.Id}",
                JsonSerializer.Serialize(alert),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                });

            _logger.LogInformation("Alert {AlertId} status updated to {Status} by {Provider}", alertId, status, providerName);
        }

        public async Task<List<EmergencyAlert>> GetActiveAlertsAsync()
        {
            var alertIdsJson = await _cache.GetStringAsync("alerts:active");
            if (string.IsNullOrEmpty(alertIdsJson))
                return new List<EmergencyAlert>();

            var alertIds = JsonSerializer.Deserialize<List<string>>(alertIdsJson) ?? new List<string>();
            var alerts = new List<EmergencyAlert>();

            foreach (var id in alertIds)
            {
                var alert = await GetAlertAsync(id);
                if (alert != null && alert.Status != AlertStatus.Closed)
                {
                    alerts.Add(alert);
                }
            }

            return alerts.OrderByDescending(a => a.Severity).ThenByDescending(a => a.Timestamp).ToList();
        }

        public async Task<EmergencyAlert?> GetAlertAsync(string alertId)
        {
            var alertJson = await _cache.GetStringAsync($"alert:{alertId}");
            if (string.IsNullOrEmpty(alertJson))
                return null;

            return JsonSerializer.Deserialize<EmergencyAlert>(alertJson);
        }

        // Alias for GetAlertAsync - used by controller
        public async Task<EmergencyAlert?> GetAlertByIdAsync(string alertId)
        {
            return await GetAlertAsync(alertId);
        }

        // Overload for manual alert creation (from API/dispatch center)
        public async Task<EmergencyAlert> CreateAlertAsync(
            EmergencyType type,
            EmergencySeverity severity,
            string location,
            double latitude,
            double longitude,
            string description,
            string contactPhone,
            string reportedBy)
        {
            var alert = new EmergencyAlert
            {
                VictimPhone = contactPhone,
                VictimMessage = description,
                Location = location,
                Latitude = latitude,
                Longitude = longitude,
                Severity = severity,
                Type = type,
                Description = description,
                EstimatedPeopleAffected = 1
            };

            // Store in cache
            await _cache.SetStringAsync(
                $"alert:{alert.Id}",
                JsonSerializer.Serialize(alert),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                });

            // Add to active alerts list
            var activeAlerts = await GetActiveAlertsAsync();
            activeAlerts.Add(alert);
            await _cache.SetStringAsync(
                "alerts:active",
                JsonSerializer.Serialize(activeAlerts.Select(a => a.Id).ToList()),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                });

            _logger.LogWarning("🚨 MANUAL ALERT CREATED by {ReportedBy}: {AlertId} - {Type} in {Location} - Severity: {Severity}",
                reportedBy, alert.Id, type, location, severity);

            return alert;
        }

        // Overload returning bool for controller
        public async Task<bool> UpdateAlertStatusAsync(string alertId, AlertStatus status, string? notes)
        {
            var alert = await GetAlertAsync(alertId);
            if (alert == null) return false;

            alert.Status = status;
            alert.Updates.Add(new AlertUpdate
            {
                Provider = "API",
                Message = notes ?? $"Status updated to {status}",
                NewStatus = status
            });

            await _cache.SetStringAsync(
                $"alert:{alert.Id}",
                JsonSerializer.Serialize(alert),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                });

            _logger.LogInformation("Alert {AlertId} status updated to {Status}", alertId, status);
            return true;
        }

        // Get alerts filtered by severity
        public async Task<List<EmergencyAlert>> GetAlertsBySeverityAsync(EmergencySeverity severity)
        {
            var allAlerts = await GetActiveAlertsAsync();
            return allAlerts.Where(a => a.Severity == severity).ToList();
        }

        private string FormatAlertNotification(EmergencyAlert alert, Models.ServiceProvider provider)
        {
            var severityEmoji = alert.Severity switch
            {
                EmergencySeverity.Critical => "🔴🔴🔴",
                EmergencySeverity.High => "🔴🔴",
                EmergencySeverity.Medium => "🟡",
                _ => "🟢"
            };

            var typeEmoji = alert.Type switch
            {
                EmergencyType.Landslide => "🏔️",
                EmergencyType.Flood => "🌊",
                EmergencyType.Fire => "🔥",
                EmergencyType.Earthquake => "🌍",
                EmergencyType.MedicalEmergency => "🏥",
                EmergencyType.Accident => "🚗",
                EmergencyType.Violence => "⚠️",
                EmergencyType.Drowning => "🌊",
                EmergencyType.BuildingCollapse => "🏚️",
                _ => "🆘"
            };

            var message = $@"{severityEmoji} *EMERGENCY ALERT* {severityEmoji}

{typeEmoji} *Type:* {alert.Type}
📍 *Location:* {alert.Location}
⏰ *Time:* {alert.Timestamp:HH:mm dd/MM/yyyy}
👥 *Est. Affected:* {alert.EstimatedPeopleAffected} people

📝 *Victim Report:*
""{alert.VictimMessage}""

📞 *Victim Contact:* {alert.VictimPhone}";

            if (alert.Latitude.HasValue && alert.Longitude.HasValue)
            {
                message += $@"

📍 *GPS Coordinates:*
Lat: {alert.Latitude:F6}
Lon: {alert.Longitude:F6}
🗺️ https://maps.google.com/?q={alert.Latitude},{alert.Longitude}";
            }

            message += $@"

🆔 *Alert ID:* {alert.Id}

⚡ *ACTION REQUIRED*
Please acknowledge and respond to this emergency.

Reply with:
• ACK - Acknowledge receipt
• DISPATCH - Team dispatched
• ONSCENE - Arrived at scene
• RESOLVED - Emergency resolved";

            return message;
        }

        private int EstimatePeopleAffected(string message)
        {
            var lowerMessage = message.ToLower();
            
            // Check for explicit numbers
            var numberMatch = Regex.Match(message, @"(\d+)\s*(people|persons|families|households|villagers)", RegexOptions.IgnoreCase);
            if (numberMatch.Success && int.TryParse(numberMatch.Groups[1].Value, out var count))
            {
                if (lowerMessage.Contains("families") || lowerMessage.Contains("households"))
                    return count * 5; // Estimate 5 people per family
                return count;
            }

            // Estimate based on keywords
            if (lowerMessage.Contains("village") || lowerMessage.Contains("community"))
                return 50;
            if (lowerMessage.Contains("many people") || lowerMessage.Contains("several"))
                return 10;
            if (lowerMessage.Contains("family") || lowerMessage.Contains("household"))
                return 5;
            if (lowerMessage.Contains("we") || lowerMessage.Contains("us"))
                return 3;

            return 1;
        }
    }
}
