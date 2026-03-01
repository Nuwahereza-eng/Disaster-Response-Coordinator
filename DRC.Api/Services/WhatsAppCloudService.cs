using DRC.Api.Interfaces;
using DRC.Api.Models;
using System.Security.Cryptography;
using WhatsappBusiness.CloudApi.Interfaces;
using WhatsappBusiness.CloudApi.Messages.Requests;
using WhatsappBusiness.CloudApi;
using System.Text;
using Microsoft.Extensions.Logging;

namespace DRC.Api.Services
{
    public class WhatsAppCloudService : IWhatAppService
    {
        private readonly IWhatsAppBusinessClient _whatsAppBusinessClient;
        private readonly IAgentService _agentService;
        private readonly ILogger<WhatsAppCloudService> _logger;
        private readonly IGeocodingService _geocodingService;
        private readonly IGooglePlacesService _placesService;

        public WhatsAppCloudService(
            IWhatsAppBusinessClient whatsAppBusinessClient, 
            IAgentService agentService,
            ILogger<WhatsAppCloudService> logger,
            IGeocodingService geocodingService,
            IGooglePlacesService placesService)
        {
            _whatsAppBusinessClient = whatsAppBusinessClient;
            _agentService = agentService;
            _logger = logger;
            _geocodingService = geocodingService;
            _placesService = placesService;
        }

        public async Task<bool> ReceiveMessage(string phone, string message)
        {
            try
            {
                _logger.LogInformation("Received WhatsApp message from {Phone}: {Message}", phone, message);
                
                var GuidGen = CreateGuidFromSeed(phone);
                
                // Check for quick commands
                var lowerMessage = message.ToLower().Trim();
                
                if (lowerMessage == "help" || lowerMessage == "menu" || lowerMessage == "start")
                {
                    await SendWelcomeMessage(phone);
                    return true;
                }
                
                if (lowerMessage == "emergency" || lowerMessage == "sos" || lowerMessage == "999")
                {
                    await SendEmergencyContacts(phone);
                    return true;
                }

                // Process through AI Agent (with action capabilities)
                var response = await _agentService.ProcessMessageAsync(GuidGen, message, userPhone: phone);
                
                // Format response with actions if any were taken
                var formattedResponse = FormatAgentResponse(response);
                
                // Split long messages (WhatsApp has 4096 char limit)
                await SendLongMessage(phone, formattedResponse);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing WhatsApp message from {Phone}", phone);
                await SendMessage(phone, "Sorry, an error occurred. Please try again or call 999 for immediate help.");
                return false;
            }
        }

        public async Task<bool> ReceiveLocation(string phone, double latitude, double longitude)
        {
            try
            {
                _logger.LogInformation("Received location from {Phone}: {Lat}, {Lon}", phone, latitude, longitude);
                
                // Find nearby facilities
                var facilities = await _placesService.GetHospitalsAsync(latitude, longitude);
                
                var response = $"📍 *Location Received*\n\n";
                response += $"🏥 *Nearby Emergency Facilities:*\n{facilities}\n\n";
                response += "📞 *Emergency Contacts:*\n";
                response += "• Police: 999 or 112\n";
                response += "• Ambulance: 911\n";
                response += "• Fire: 112\n\n";
                response += "_Reply with your situation for more help._";
                
                await SendLongMessage(phone, response);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing location from {Phone}", phone);
                await SendMessage(phone, "Could not process your location. Please tell me your district or area name.");
                return false;
            }
        }

        private async Task SendWelcomeMessage(string phone)
        {
            var welcome = "🆘 *Uganda Disaster Response Coordinator*\n\n";
            welcome += "Hello! I'm Direco, your emergency assistant. I can help you with:\n\n";
            welcome += "🏥 Find nearby hospitals\n";
            welcome += "⛺ Locate emergency shelters\n";
            welcome += "🚔 Find police stations\n";
            welcome += "⚠️ Get disaster information\n";
            welcome += "📍 Share your location for help\n\n";
            welcome += "*Quick Commands:*\n";
            welcome += "• Type *SOS* or *999* for emergency contacts\n";
            welcome += "• Send your *location* 📍 for nearest help\n";
            welcome += "• Tell me your *district* (e.g., 'I'm in Kampala')\n\n";
            welcome += "_What help do you need today?_";
            
            await SendMessage(phone, welcome);
        }

        private async Task SendEmergencyContacts(string phone)
        {
            var emergency = "🚨 *UGANDA EMERGENCY CONTACTS* 🚨\n\n";
            emergency += "☎️ *Immediate Help:*\n";
            emergency += "• Police: *999* or *112*\n";
            emergency += "• Ambulance: *911*\n";
            emergency += "• Fire Brigade: *112*\n\n";
            emergency += "🏥 *Health Emergencies:*\n";
            emergency += "• Mulago Hospital: +256-414-541-188\n";
            emergency += "• Kampala Ambulance: 0800-100-999\n\n";
            emergency += "🆘 *Disaster Response:*\n";
            emergency += "• Uganda Red Cross: 0800-100-250\n";
            emergency += "• NECOC: 0800-100-066\n";
            emergency += "• OPM Disaster Line: +256-417-774-500\n\n";
            emergency += "🛡️ *Other Helplines:*\n";
            emergency += "• GBV Hotline: 0800-199-199\n";
            emergency += "• Child Helpline (SAUTI): 116\n\n";
            emergency += "_Stay safe! Send your location or tell me your area for nearby help._";
            
            await SendMessage(phone, emergency);
        }

        private async Task SendLongMessage(string phone, string message)
        {
            const int maxLength = 4000; // Leave some buffer from 4096 limit
            
            if (message.Length <= maxLength)
            {
                await SendMessage(phone, message);
                return;
            }

            // Split by paragraphs first
            var paragraphs = message.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            var currentChunk = new StringBuilder();

            foreach (var para in paragraphs)
            {
                if (currentChunk.Length + para.Length + 2 > maxLength)
                {
                    await SendMessage(phone, currentChunk.ToString());
                    await Task.Delay(500); // Small delay between messages
                    currentChunk.Clear();
                }
                
                if (currentChunk.Length > 0)
                    currentChunk.Append("\n\n");
                currentChunk.Append(para);
            }

            if (currentChunk.Length > 0)
            {
                await SendMessage(phone, currentChunk.ToString());
            }
        }

        public async Task<bool> SendTemplateMessage(string phone, string template, List<TextMessageComponent> parameters = null)
        {
            TextTemplateMessageRequest textTemplateMessage = new()
            {
                To = phone,
                Template = new()
                {
                    Name = template,
                    Language = new()
                    {
                        Code = LanguageCode.English_US
                    }
                },
            };

            if (parameters is not null)
            {
                textTemplateMessage.Template.Components = parameters;
            }

            var results = await _whatsAppBusinessClient.SendTextMessageTemplateAsync(textTemplateMessage);
            return true;
        }

        public async Task<bool> SendMessage(string phone, string message)
        {
            TextMessageRequest textMessageRequest = new()
            {
                To = phone,
                Text = new()
                {
                    Body = message,
                    PreviewUrl = false
                }
            };

            var results = await _whatsAppBusinessClient.SendTextMessageAsync(textMessageRequest);
            _logger.LogInformation("Sent WhatsApp message to {Phone}", phone);
            return true;
        }

        private string FormatAgentResponse(AgentResponse response)
        {
            var sb = new StringBuilder();
            
            // Add actions taken if any
            if (response.ActionsTaken?.Any() == true)
            {
                sb.AppendLine("🤖 *Actions Taken:*");
                foreach (var action in response.ActionsTaken)
                {
                    var statusEmoji = action.Status switch
                    {
                        AgentActionStatus.Completed => "✅",
                        AgentActionStatus.InProgress => "⏳",
                        AgentActionStatus.Failed => "❌",
                        _ => "📋"
                    };
                    sb.AppendLine($"{statusEmoji} {action.Description}");
                }
                sb.AppendLine();
            }
            
            // Add the main message
            sb.Append(response.Message);
            
            return sb.ToString();
        }

        private Guid CreateGuidFromSeed(string seed)
        {
            using (var hash = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(seed);
                byte[] hashBytes = hash.ComputeHash(bytes);
                return new Guid(hashBytes[..16]);
            }
        }
    }
}
