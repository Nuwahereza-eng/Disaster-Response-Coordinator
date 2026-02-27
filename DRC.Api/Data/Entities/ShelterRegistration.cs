using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DRC.Api.Data.Entities
{
    public enum RegistrationStatus
    {
        Pending = 0,
        Approved = 1,
        CheckedIn = 2,
        CheckedOut = 3,
        Cancelled = 4
    }

    public class ShelterRegistration
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public Guid RegistrationGuid { get; set; } = Guid.NewGuid();

        public int? UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        [MaxLength(100)]
        public string? FullName { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [Required]
        public int FamilySize { get; set; } = 1;

        public int Adults { get; set; } = 1;
        public int Children { get; set; } = 0;
        public int Elderly { get; set; } = 0;

        [MaxLength(500)]
        public string? SpecialNeeds { get; set; }

        [MaxLength(500)]
        public string? MedicalConditions { get; set; }

        [MaxLength(200)]
        public string? ShelterName { get; set; }

        [MaxLength(500)]
        public string? ShelterAddress { get; set; }

        [Required]
        public RegistrationStatus Status { get; set; } = RegistrationStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CheckInAt { get; set; }
        public DateTime? CheckOutAt { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}
