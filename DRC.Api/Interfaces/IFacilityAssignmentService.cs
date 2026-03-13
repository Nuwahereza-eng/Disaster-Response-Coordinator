using DRC.Api.Data.Entities;

namespace DRC.Api.Interfaces
{
    public interface IFacilityAssignmentService
    {
        /// <summary>
        /// Find the nearest appropriate facility for an emergency request
        /// </summary>
        Task<Facility?> FindNearestFacilityForEmergencyAsync(EmergencyType emergencyType, double? latitude, double? longitude);

        /// <summary>
        /// Find the nearest shelter with available capacity
        /// </summary>
        Task<Facility?> FindNearestShelterAsync(double? latitude, double? longitude, int numberOfPeople);

        /// <summary>
        /// Find the nearest evacuation point
        /// </summary>
        Task<Facility?> FindNearestEvacuationPointAsync(double? latitude, double? longitude);

        /// <summary>
        /// Assign a specific facility to an emergency request
        /// </summary>
        Task<bool> AssignFacilityToEmergencyAsync(int emergencyRequestId, int facilityId);

        /// <summary>
        /// Assign a specific facility to a shelter registration
        /// </summary>
        Task<bool> AssignFacilityToShelterAsync(int shelterRegistrationId, int facilityId);

        /// <summary>
        /// Assign a specific facility to an evacuation request
        /// </summary>
        Task<bool> AssignFacilityToEvacuationAsync(int evacuationRequestId, int facilityId);

        /// <summary>
        /// Get all facilities suitable for a given request type
        /// </summary>
        Task<List<Facility>> GetSuitableFacilitiesAsync(string requestType, double? latitude = null, double? longitude = null);
    }
}
