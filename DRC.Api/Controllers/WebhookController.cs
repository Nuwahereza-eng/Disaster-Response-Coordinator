using DRC.Api.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text.Json;
using WhatsappBusiness.CloudApi.Interfaces;
using WhatsappBusiness.CloudApi.Messages.Requests;
using WhatsappBusiness.CloudApi.Webhook;

namespace DRC.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WebhookController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<WebhookController> _logger;

        // Deduplication: track recently processed message IDs to prevent Meta retries
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _processedMessages = new();

        public WebhookController(IConfiguration configuration, ILogger<WebhookController> logger)
        {
            _configuration = configuration;
            _logger = logger;

            // Clean up old entries (older than 5 minutes)
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            foreach (var key in _processedMessages.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList())
            {
                _processedMessages.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// WhatsApp Webhook Verification (required by Meta)
        /// </summary>
        [AllowAnonymous]
        [HttpGet("whatsapp")]
        public async Task<IActionResult> GetWhatsApp(
            [FromQuery(Name = "hub.mode")] string hubMode, 
            [FromQuery(Name = "hub.challenge")] int hubChallenge, 
            [FromQuery(Name = "hub.verify_token")] string hubVerifyToken)
        {
            _logger.LogInformation("WhatsApp webhook verification request received");
            
            var key = _configuration["Apps:Meta:Key"];
            if (!hubVerifyToken.Equals(key))
            {
                _logger.LogWarning("WhatsApp webhook verification failed - invalid token");
                return Forbid();
            }

            _logger.LogInformation("WhatsApp webhook verified successfully");
            return Ok(hubChallenge);
        }

        /// <summary>
        /// Receives incoming WhatsApp messages and forwards to AI assistant
        /// </summary>
        [AllowAnonymous]
        [HttpPost("whatsapp")]
        public async Task<IActionResult> PostWhatsApp(
            IWhatsAppBusinessClient whatsAppBusinessClient, 
            IWhatAppService whatAppService, 
            [FromBody] dynamic messageReceived)
        {
            if (messageReceived is null)
            {
                return BadRequest(new { Message = "Message not received" });
            }

            try
            {
                string msg = messageReceived.ToString();
                _logger.LogDebug("Received WhatsApp webhook: {Message}", msg);
                
                JsonDocument doc = JsonDocument.Parse(msg);

                if (doc.RootElement.TryGetProperty("entry", out var entries) && entries.EnumerateArray().Any())
                {
                    var firstEntry = entries.EnumerateArray().First();

                    if (firstEntry.TryGetProperty("changes", out var changes) && changes.EnumerateArray().Any())
                    {
                        var firstChange = changes.EnumerateArray().First();

                        if (firstChange.TryGetProperty("value", out var value))
                        {
                            // Skip status updates (delivery receipts, etc.)
                            bool isStatusesNull = !value.TryGetProperty("statuses", out var statuses) || 
                                statuses.ValueKind == JsonValueKind.Null || 
                                (statuses.ValueKind == JsonValueKind.Array && !statuses.EnumerateArray().Any());

                            if (isStatusesNull && value.TryGetProperty("messages", out var messages) && messages.EnumerateArray().Any())
                            {
                                var firstMessage = messages.EnumerateArray().First();
                                var from = firstMessage.GetProperty("from").GetString() ?? "";

                                if (firstMessage.TryGetProperty("type", out var type))
                                {
                                    string messageType = type.GetString() ?? "";
                                    _logger.LogInformation("Processing WhatsApp message type: {Type} from {From}", messageType, from);

                                    // Handle TEXT messages
                                    if (messageType.Equals("text"))
                                    {
                                        var textMessageReceived = JsonConvert.DeserializeObject<TextMessageReceived>(Convert.ToString(messageReceived)) as TextMessageReceived;
                                        var textMessages = new List<TextMessage>(textMessageReceived?.Entry.SelectMany(x => x.Changes).SelectMany(x => x.Value.Messages) ?? []);

                                        var metadata = textMessages.SingleOrDefault();
                                        if (metadata != null)
                                        {
                                            // Deduplication: skip if already processed
                                            if (!_processedMessages.TryAdd(metadata.Id, DateTime.UtcNow))
                                            {
                                                _logger.LogInformation("⏭️ Skipping duplicate message {MessageId} from {From}", metadata.Id, from);
                                                return Ok();
                                            }

                                            // Mark as read
                                            await MarkAsRead(whatsAppBusinessClient, metadata.Id);
                                            
                                            // Process message in BACKGROUND — return 200 immediately to Meta
                                            _ = Task.Run(async () =>
                                            {
                                                try
                                                {
                                                    await whatAppService.ReceiveMessage(metadata.From, metadata.Text.Body);
                                                }
                                                catch (Exception ex)
                                                {
                                                    _logger.LogError(ex, "Background error processing message from {From}", metadata.From);
                                                }
                                            });
                                            
                                            return Ok(new { Message = "Text message accepted" });
                                        }
                                    }
                                    // Handle LOCATION messages
                                    else if (messageType.Equals("location"))
                                    {
                                        if (firstMessage.TryGetProperty("location", out var location))
                                        {
                                            var latitude = location.GetProperty("latitude").GetDouble();
                                            var longitude = location.GetProperty("longitude").GetDouble();
                                            var messageId = firstMessage.GetProperty("id").GetString() ?? "";

                                            // Deduplication
                                            if (!_processedMessages.TryAdd(messageId, DateTime.UtcNow))
                                            {
                                                _logger.LogInformation("⏭️ Skipping duplicate location {MessageId}", messageId);
                                                return Ok();
                                            }

                                            // Mark as read
                                            await MarkAsRead(whatsAppBusinessClient, messageId);
                                            
                                            // Process location in BACKGROUND
                                            _ = Task.Run(async () =>
                                            {
                                                try
                                                {
                                                    await whatAppService.ReceiveLocation(from, latitude, longitude);
                                                }
                                                catch (Exception ex)
                                                {
                                                    _logger.LogError(ex, "Background error processing location from {From}", from);
                                                }
                                            });
                                            
                                            return Ok(new { Message = "Location message accepted" });
                                        }
                                    }
                                    // Handle INTERACTIVE messages (button clicks, list selections)
                                    else if (messageType.Equals("interactive"))
                                    {
                                        if (firstMessage.TryGetProperty("interactive", out var interactive))
                                        {
                                            var interactiveType = interactive.GetProperty("type").GetString();
                                            string responseText = "";
                                            var messageId = firstMessage.GetProperty("id").GetString() ?? "";

                                            if (interactiveType == "button_reply")
                                            {
                                                responseText = interactive.GetProperty("button_reply").GetProperty("title").GetString() ?? "";
                                            }
                                            else if (interactiveType == "list_reply")
                                            {
                                                responseText = interactive.GetProperty("list_reply").GetProperty("title").GetString() ?? "";
                                            }

                                            if (!string.IsNullOrEmpty(responseText))
                                            {
                                                // Deduplication
                                                if (!_processedMessages.TryAdd(messageId, DateTime.UtcNow))
                                                {
                                                    _logger.LogInformation("⏭️ Skipping duplicate interactive {MessageId}", messageId);
                                                    return Ok();
                                                }

                                                await MarkAsRead(whatsAppBusinessClient, messageId);
                                                
                                                // Process in BACKGROUND
                                                _ = Task.Run(async () =>
                                                {
                                                    try
                                                    {
                                                        await whatAppService.ReceiveMessage(from, responseText);
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        _logger.LogError(ex, "Background error processing interactive from {From}", from);
                                                    }
                                                });
                                                
                                                return Ok(new { Message = "Interactive message accepted" });
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing WhatsApp webhook");
                return Ok(); // Return OK to prevent Meta from retrying
            }
        }

        /// <summary>
        /// Africa's Talking SMS Delivery Report Callback
        /// AT sends POST with: id, status, failureReason, phoneNumber, networkCode
        /// </summary>
        [AllowAnonymous]
        [HttpPost("africastalking/delivery")]
        public IActionResult AfricasTalkingDeliveryReport(
            [FromForm] string? id,
            [FromForm] string? status,
            [FromForm] string? phoneNumber,
            [FromForm] string? failureReason,
            [FromForm] string? networkCode)
        {
            _logger.LogInformation(
                "📱 AT Delivery Report — Id: {Id}, Status: {Status}, Phone: {Phone}, Failure: {Failure}, Network: {Network}",
                id, status, phoneNumber, failureReason, networkCode);

            if (!string.IsNullOrEmpty(failureReason) && !status?.Equals("Success", StringComparison.OrdinalIgnoreCase) == true)
            {
                _logger.LogWarning("📱 SMS delivery FAILED to {Phone}: {Reason}", phoneNumber, failureReason);
            }

            return Ok(); // Always return 200 so AT doesn't retry
        }

        /// <summary>
        /// Africa's Talking Incoming SMS Callback (for replies)
        /// </summary>
        [AllowAnonymous]
        [HttpPost("africastalking/incoming")]
        public IActionResult AfricasTalkingIncoming(
            [FromForm] string? from,
            [FromForm] string? to,
            [FromForm] string? text,
            [FromForm] string? date,
            [FromForm] string? id,
            [FromForm] string? linkId)
        {
            _logger.LogInformation(
                "📱 AT Incoming SMS — From: {From}, To: {To}, Text: {Text}, Date: {Date}",
                from, to, text, date);

            // For now just log — can be extended to process replies
            return Ok();
        }

        private async Task MarkAsRead(IWhatsAppBusinessClient client, string messageId)
        {
            try
            {
                await client.MarkMessageAsReadAsync(new MarkMessageRequest
                {
                    MessageId = messageId,
                    Status = "read"
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to mark message {MessageId} as read", messageId);
            }
        }
    }
}
