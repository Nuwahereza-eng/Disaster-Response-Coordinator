using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DRC.Api.Data.Entities
{
    public enum EmergencyType
    {
        Fire = 0,
        Flood = 1,
        Earthquake = 2,
        Medical = 3,
        Violence = 4,
        Accident = 5,
        Other = 6
    }

    public enum EmergencySeverity
    {
        Low = 0,
        Medium = 1,
        High = 2,
        Critical = 3
    }

    public enum RequestStatus
    {
        Pending = 0,
        Acknowledged = 1,
        Dispatched = 2,
        InProgress = 3,
        Resolved = 4,
        Cancelled = 5
    }

    public class EmergencyRequest
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public Guid RequestGuid { get; set; } = Guid.NewGuid();

        public int? UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        [MaxLength(20)]
        public string? UserPhone { get; set; }

        [Required]
        public EmergencyType Type { get; set; }

        [Required]
        public EmergencySeverity Severity { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Location { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        [Required]
        public RequestStatus Status { get; set; } = RequestStatus.Pending;

        [MaxLength(100)]
        public string? AssignedResponder { get; set; }

        [MaxLength(500)]
        public string? ResponseNotes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AcknowledgedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }

        // Services dispatched
        public bool AmbulanceDispatched { get; set; }
        public bool FireBrigadeDispatched { get; set; }
        public bool PoliceDispatched { get; set; }

        // Assigned facility
        public int? AssignedFacilityId { get; set; }

        [ForeignKey("AssignedFacilityId")]
        public Facility? AssignedFacility { get; set; }
    }
}
