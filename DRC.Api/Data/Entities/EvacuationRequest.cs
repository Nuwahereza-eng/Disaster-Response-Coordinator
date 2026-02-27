using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DRC.Api.Data.Entities
{
    public enum EvacuationStatus
    {
        Requested = 0,
        Acknowledged = 1,
        VehicleDispatched = 2,
        EnRoute = 3,
        Arrived = 4,
        InTransit = 5,
        Completed = 6,
        Cancelled = 7
    }

    public enum EvacuationPriority
    {
        Normal = 0,
        High = 1,
        Critical = 2
    }

    public class EvacuationRequest
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public Guid RequestGuid { get; set; } = Guid.NewGuid();

        public int? UserId { get; set; }

        [ForeignKey("UserId")]
        public User? User { get; set; }

        [MaxLength(100)]
        public string? FullName { get; set; }

        [MaxLength(20)]
        public string? Phone { get; set; }

        [Required]
        public int NumberOfPeople { get; set; } = 1;

        public bool HasElderly { get; set; }
        public bool HasChildren { get; set; }
        public bool HasDisabled { get; set; }
        public bool NeedsMedicalAssistance { get; set; }

        [MaxLength(500)]
        public string? SpecialRequirements { get; set; }

        [Required]
        [MaxLength(500)]
        public string PickupLocation { get; set; } = string.Empty;

        public double? PickupLatitude { get; set; }
        public double? PickupLongitude { get; set; }

        [MaxLength(500)]
        public string? DestinationLocation { get; set; }

        public double? DestinationLatitude { get; set; }
        public double? DestinationLongitude { get; set; }

        [Required]
        public EvacuationStatus Status { get; set; } = EvacuationStatus.Requested;

        [Required]
        public EvacuationPriority Priority { get; set; } = EvacuationPriority.Normal;

        [MaxLength(100)]
        public string? AssignedVehicle { get; set; }

        [MaxLength(100)]
        public string? DriverName { get; set; }

        [MaxLength(20)]
        public string? DriverPhone { get; set; }

        public DateTime? EstimatedArrival { get; set; }
        public DateTime? ActualArrival { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}
