using DRC.Api.Models;

namespace DRC.Api.Interfaces
{
    public interface IEmergencyAlertService
    {
        /// <summary>
        /// Analyzes a message and detects if it's an emergency
        /// </summary>
        Task<(bool IsEmergency, EmergencySeverity Severity, EmergencyType Type)> DetectEmergencyAsync(string message);
        
        /// <summary>
        /// Creates and dispatches an emergency alert from WhatsApp message
        /// </summary>
        Task<EmergencyAlert> CreateAlertAsync(
            string victimPhone,
            string message,
            string location,
            double? latitude,
            double? longitude,
            EmergencySeverity severity,
            EmergencyType type);
        
        /// <summary>
        /// Creates an alert manually (from dispatch center/API)
        /// </summary>
        Task<EmergencyAlert> CreateAlertAsync(
            EmergencyType type,
            EmergencySeverity severity,
            string location,
            double latitude,
            double longitude,
            string description,
            string contactPhone,
            string reportedBy);
        
        /// <summary>
        /// Notifies all relevant service providers about an emergency
        /// </summary>
        Task NotifyServiceProvidersAsync(EmergencyAlert alert);
        
        /// <summary>
        /// Gets service providers for a specific location and emergency type
        /// </summary>
        Task<List<Models.ServiceProvider>> GetRelevantProvidersAsync(string location, EmergencyType type);
        
        /// <summary>
        /// Updates the status of an alert (with provider info)
        /// </summary>
        Task UpdateAlertStatusAsync(string alertId, AlertStatus status, string providerName, string message);
        
        /// <summary>
        /// Updates the status of an alert (simple update, returns success)
        /// </summary>
        Task<bool> UpdateAlertStatusAsync(string alertId, AlertStatus status, string? notes);
        
        /// <summary>
        /// Gets all active alerts
        /// </summary>
        Task<List<EmergencyAlert>> GetActiveAlertsAsync();
        
        /// <summary>
        /// Gets alerts filtered by severity
        /// </summary>
        Task<List<EmergencyAlert>> GetAlertsBySeverityAsync(EmergencySeverity severity);
        
        /// <summary>
        /// Gets a specific alert by ID
        /// </summary>
        Task<EmergencyAlert?> GetAlertByIdAsync(string alertId);
    }
}
