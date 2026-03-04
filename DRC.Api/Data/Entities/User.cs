using System.ComponentModel.DataAnnotations;

namespace DRC.Api.Data.Entities
{
    public enum UserRole
    {
        User = 0,
        Responder = 1,
        Admin = 2,
        Judge = 3
    }

    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Phone]
        [MaxLength(20)]
        public string Phone { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public UserRole Role { get; set; } = UserRole.User;

        [MaxLength(500)]
        public string? Address { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }

        // Navigation properties
        public ICollection<EmergencyRequest> EmergencyRequests { get; set; } = new List<EmergencyRequest>();
        public ICollection<ShelterRegistration> ShelterRegistrations { get; set; } = new List<ShelterRegistration>();
        public ICollection<EvacuationRequest> EvacuationRequests { get; set; } = new List<EvacuationRequest>();
        public ICollection<EmergencyContact> EmergencyContacts { get; set; } = new List<EmergencyContact>();
        public ICollection<AlertNotification> AlertNotifications { get; set; } = new List<AlertNotification>();
        public ICollection<ChatMessage> ChatMessages { get; set; } = new List<ChatMessage>();
    }
}
