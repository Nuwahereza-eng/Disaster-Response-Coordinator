namespace DRC.App.Models
{
    public class FacilityFormModel
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Description { get; set; }
        public string? ServicesOffered { get; set; }
        public string? OperatingHours { get; set; }
        public bool Is24Hours { get; set; }
        public int? Capacity { get; set; }
        public bool IsOperational { get; set; } = true;
    }
}
