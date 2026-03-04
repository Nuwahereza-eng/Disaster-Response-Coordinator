using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DRC.Api.Data.Entities
{
    public enum ContactRelationship
    {
        Spouse = 0,
        Parent = 1,
        Child = 2,
        Sibling = 3,
        Friend = 4,
        Colleague = 5,
        Neighbor = 6,
        Other = 7
    }

    public class EmergencyContact
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public User User { get; set; } = null!;

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [Phone]
        [MaxLength(20)]
        public string Phone { get; set; } = string.Empty;

        [EmailAddress]
        [MaxLength(255)]
        public string? Email { get; set; }

        [Phone]
        [MaxLength(20)]
        public string? WhatsAppNumber { get; set; }

        [Required]
        public ContactRelationship Relationship { get; set; }

        public bool IsPrimary { get; set; }
        public bool NotifyOnEmergency { get; set; } = true;
        public bool NotifyOnEvacuation { get; set; } = true;
        public bool NotifyOnShelter { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
