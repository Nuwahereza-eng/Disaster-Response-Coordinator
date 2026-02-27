namespace DRC.Api.Models
{
    /// <summary>
    /// Represents an action taken by the agent on behalf of the user
    /// </summary>
    public class AgentAction
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ToolName { get; set; } = "";
        public string Description { get; set; } = "";
        public Dictionary<string, object> Parameters { get; set; } = new();
        public AgentActionStatus Status { get; set; } = AgentActionStatus.Pending;
        public string? Result { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }

    public enum AgentActionStatus
    {
        Pending,
        InProgress,
        Completed,
        Failed
    }

    /// <summary>
    /// Session state for an agent conversation, including actions taken
    /// </summary>
    public class AgentSession
    {
        public Guid SessionId { get; set; } = Guid.NewGuid();
        public int? UserId { get; set; }
        public string UserPhone { get; set; } = "";
        public string? UserName { get; set; }
        public string? UserLocation { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public List<AgentMessage> Messages { get; set; } = new();
        public List<AgentAction> ActionsTaken { get; set; } = new();
        public List<string> ActiveAlertIds { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    }

    public class AgentMessage
    {
        public string Role { get; set; } = "user"; // user, agent, system
        public string Content { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public List<AgentAction>? ActionsInMessage { get; set; }
    }

    /// <summary>
    /// Response from the agent including message and any actions taken
    /// </summary>
    public class AgentResponse
    {
        public Guid SessionId { get; set; }
        public string Message { get; set; } = "";
        public List<AgentAction> ActionsTaken { get; set; } = new();
        public bool IsEmergency { get; set; }
        public EmergencySeverity? Severity { get; set; }
    }

    /// <summary>
    /// Registration for shelter/assistance
    /// </summary>
    public class AssistanceRegistration
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public Guid SessionId { get; set; }
        public string UserPhone { get; set; } = "";
        public string? UserName { get; set; }
        public string Location { get; set; } = "";
        public int NumberOfPeople { get; set; } = 1;
        public List<string> SpecialNeeds { get; set; } = new(); // elderly, children, medical, disabled
        public AssistanceType Type { get; set; }
        public RegistrationStatus Status { get; set; } = RegistrationStatus.Pending;
        public string? AssignedShelter { get; set; }
        public string? AssignedShelterAddress { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public enum AssistanceType
    {
        Shelter,
        MedicalCare,
        FoodAndWater,
        Evacuation,
        Rescue,
        GeneralHelp
    }

    public enum RegistrationStatus
    {
        Pending,
        Confirmed,
        InTransit,
        Arrived,
        Completed,
        Cancelled
    }

    /// <summary>
    /// Emergency contact notification request
    /// </summary>
    public class ContactNotification
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public Guid SessionId { get; set; }
        public string ContactPhone { get; set; } = "";
        public string? ContactName { get; set; }
        public string Message { get; set; } = "";
        public NotificationStatus Status { get; set; } = NotificationStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? SentAt { get; set; }
    }

    public enum NotificationStatus
    {
        Pending,
        Sent,
        Delivered,
        Failed
    }

    /// <summary>
    /// Represents a chat message in user's history
    /// </summary>
    public class ChatHistoryItem
    {
        public int Id { get; set; }
        public Guid SessionId { get; set; }
        public string Role { get; set; } = "user";
        public string Content { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public List<AgentAction>? Actions { get; set; }
    }
}
