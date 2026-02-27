namespace DRC.Api.Models
{
    public class EmergencyAlert
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public EmergencySeverity Severity { get; set; }
        public EmergencyType Type { get; set; }
        public string Location { get; set; } = "";
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string Description { get; set; } = "";
        public string VictimPhone { get; set; } = "";
        public string VictimMessage { get; set; } = "";
        public int EstimatedPeopleAffected { get; set; } = 1;
        public AlertStatus Status { get; set; } = AlertStatus.New;
        public List<string> NotifiedProviders { get; set; } = new();
        public List<AlertUpdate> Updates { get; set; } = new();
    }

    public class AlertUpdate
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Provider { get; set; } = "";
        public string Message { get; set; } = "";
        public AlertStatus NewStatus { get; set; }
    }

    public enum EmergencySeverity
    {
        Low = 1,        // Non-urgent request for information
        Medium = 2,     // Needs assistance but not life-threatening
        High = 3,       // Urgent - potential danger
        Critical = 4    // Life-threatening - immediate response needed
    }

    public enum EmergencyType
    {
        Unknown,
        Landslide,
        Flood,
        Fire,
        Earthquake,
        DiseaseOutbreak,
        Accident,
        Violence,
        MedicalEmergency,
        Drowning,
        BuildingCollapse,
        MissingPerson
    }

    public enum AlertStatus
    {
        New,
        Acknowledged,
        Dispatched,
        OnScene,
        Resolved,
        Closed
    }

    public class ServiceProvider
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public ServiceProviderType Type { get; set; }
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public string WhatsAppNumber { get; set; } = "";
        public List<string> CoveredDistricts { get; set; } = new();
        public List<EmergencyType> HandledEmergencies { get; set; } = new();
        public bool IsActive { get; set; } = true;
    }

    public enum ServiceProviderType
    {
        Police,
        Ambulance,
        FireBrigade,
        Hospital,
        RedCross,
        NECOC,
        DistrictDisasterCommittee,
        UPDF,
        NGO,
        CommunityLeader
    }
}
