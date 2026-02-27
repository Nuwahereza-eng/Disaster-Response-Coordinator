using System.ComponentModel.DataAnnotations;

namespace DRC.Api.Data.Entities
{
    public enum FacilityType
    {
        Hospital = 0,
        Clinic = 1,
        Pharmacy = 2,
        PoliceStation = 3,
        FireStation = 4,
        Shelter = 5,
        EvacuationPoint = 6,
        FoodDistribution = 7,
        WaterPoint = 8,
        Other = 9
    }

    public class Facility
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public FacilityType Type { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        [Phone]
        [MaxLength(20)]
        public string? Phone { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public int? Capacity { get; set; }
        public int? CurrentOccupancy { get; set; }

        public bool IsOperational { get; set; } = true;
        public bool Is24Hours { get; set; }

        [MaxLength(200)]
        public string? OperatingHours { get; set; }

        [MaxLength(500)]
        public string? ServicesOffered { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUpdatedAt { get; set; }
    }
}
