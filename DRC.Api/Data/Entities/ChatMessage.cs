using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DRC.Api.Data.Entities
{
    /// <summary>
    /// Persistent storage for chat messages between users and the AI agent
    /// </summary>
    public class ChatMessage
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// The session this message belongs to
        /// </summary>
        [Required]
        public Guid SessionId { get; set; }

        /// <summary>
        /// Optional user ID if authenticated
        /// </summary>
        public int? UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        /// <summary>
        /// Role: "user", "agent", or "system"
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Role { get; set; } = "user";

        /// <summary>
        /// The message content
        /// </summary>
        [Required]
        public string Content { get; set; } = "";

        /// <summary>
        /// User's phone number if available
        /// </summary>
        [MaxLength(20)]
        public string? UserPhone { get; set; }

        /// <summary>
        /// User's location if available
        /// </summary>
        [MaxLength(500)]
        public string? UserLocation { get; set; }

        /// <summary>
        /// GPS latitude if available
        /// </summary>
        public double? Latitude { get; set; }

        /// <summary>
        /// GPS longitude if available
        /// </summary>
        public double? Longitude { get; set; }

        /// <summary>
        /// JSON of actions taken during this message
        /// </summary>
        public string? ActionsJson { get; set; }

        /// <summary>
        /// When the message was created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
