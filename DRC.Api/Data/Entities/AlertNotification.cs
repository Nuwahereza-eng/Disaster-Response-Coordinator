using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DRC.Api.Data.Entities
{
    public enum NotificationType
    {
        Emergency = 0,
        Evacuation = 1,
        Shelter = 2,
        SafetyAlert = 3,
        StatusUpdate = 4,
        General = 5
    }

    public enum NotificationChannel
    {
        SMS = 0,
        WhatsApp = 1,
        Email = 2,
        Push = 3
    }

    public enum NotificationStatus
    {
        Pending = 0,
        Sent = 1,
        Delivered = 2,
        Failed = 3
    }

    public class AlertNotification
    {
        [Key]
        public int Id { get; set; }

        public int? UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        [Required]
        [MaxLength(20)]
        public string RecipientPhone { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? RecipientEmail { get; set; }

        [MaxLength(100)]
        public string? RecipientName { get; set; }

        [Required]
        public NotificationType Type { get; set; }

        [Required]
        public NotificationChannel Channel { get; set; }

        [Required]
        [MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        [MaxLength(2000)]
        public string Message { get; set; } = string.Empty;

        [Required]
        public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

        [MaxLength(500)]
        public string? ErrorMessage { get; set; }

        public int? RelatedEmergencyRequestId { get; set; }
        public int? RelatedEvacuationRequestId { get; set; }
        public int? RelatedShelterRegistrationId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
    }
}
