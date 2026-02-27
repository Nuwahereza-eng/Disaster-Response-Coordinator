using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DRC.Api.Data.Entities
{
    public class AgentSession
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public Guid SessionGuid { get; set; } = Guid.NewGuid();

        public int? UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        [MaxLength(20)]
        public string? UserPhone { get; set; }

        [MaxLength(500)]
        public string? UserLocation { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;

        // JSON storage for conversation history
        [MaxLength(50000)]
        public string? ConversationHistoryJson { get; set; }

        // JSON storage for actions taken
        [MaxLength(10000)]
        public string? ActionsTakenJson { get; set; }
    }
}
