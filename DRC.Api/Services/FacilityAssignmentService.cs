using DRC.Api.Data;
using DRC.Api.Data.Entities;
using DRC.Api.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DRC.Api.Services
{
    public class FacilityAssignmentService : IFacilityAssignmentService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<FacilityAssignmentService> _logger;

        public FacilityAssignmentService(ApplicationDbContext dbContext, ILogger<FacilityAssignmentService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<Facility?> FindNearestFacilityForEmergencyAsync(EmergencyType emergencyType, double? latitude, double? longitude)
        {
            // Map emergency type to facility types
            var facilityTypes = emergencyType switch
            {
                EmergencyType.Fire => new[] { FacilityType.FireStation },
                EmergencyType.Medical => new[] { FacilityType.Hospital, FacilityType.Clinic },
                EmergencyType.Violence => new[] { FacilityType.PoliceStation },
                EmergencyType.Accident => new[] { FacilityType.Hospital, FacilityType.Clinic, FacilityType.PoliceStation },
                EmergencyType.Flood => new[] { FacilityType.Shelter, FacilityType.EvacuationPoint },
                EmergencyType.Earthquake => new[] { FacilityType.Shelter, FacilityType.EvacuationPoint, FacilityType.Hospital },
                _ => new[] { FacilityType.Hospital, FacilityType.PoliceStation, FacilityType.Clinic }
            };

            var facilities = await _dbContext.Facilities
                .Where(f => f.IsOperational && facilityTypes.Contains(f.Type))
                .ToListAsync();

            if (!facilities.Any())
            {
                // Fall back to any operational facility
                facilities = await _dbContext.Facilities
                    .Where(f => f.IsOperational)
                    .ToListAsync();
            }

            if (!facilities.Any())
            {
                _logger.LogWarning("No operational facilities available for emergency type {EmergencyType}", emergencyType);
                return null;
            }

            return FindNearest(facilities, latitude, longitude);
        }

        public async Task<Facility?> FindNearestShelterAsync(double? latitude, double? longitude, int numberOfPeople)
        {
            var shelters = await _dbContext.Facilities
                .Where(f => f.IsOperational && f.Type == FacilityType.Shelter)
                .Where(f => !f.Capacity.HasValue || (f.Capacity - (f.CurrentOccupancy ?? 0)) >= numberOfPeople)
                .ToListAsync();

            if (!shelters.Any())
            {
                // Try evacuation points as fallback
                shelters = await _dbContext.Facilities
                    .Where(f => f.IsOperational && f.Type == FacilityType.EvacuationPoint)
                    .ToListAsync();
            }

            if (!shelters.Any())
            {
                _logger.LogWarning("No available shelters for {NumberOfPeople} people", numberOfPeople);
                return null;
            }

            return FindNearest(shelters, latitude, longitude);
        }

        public async Task<Facility?> FindNearestEvacuationPointAsync(double? latitude, double? longitude)
        {
            var evacuationPoints = await _dbContext.Facilities
                .Where(f => f.IsOperational && (f.Type == FacilityType.EvacuationPoint || f.Type == FacilityType.Shelter))
                .ToListAsync();

            if (!evacuationPoints.Any())
            {
                _logger.LogWarning("No evacuation points available");
                return null;
            }

            return FindNearest(evacuationPoints, latitude, longitude);
        }

        public async Task<bool> AssignFacilityToEmergencyAsync(int emergencyRequestId, int facilityId)
        {
            var request = await _dbContext.EmergencyRequests.FindAsync(emergencyRequestId);
            if (request == null)
            {
                _logger.LogWarning("Emergency request {Id} not found", emergencyRequestId);
                return false;
            }

            var facility = await _dbContext.Facilities.FindAsync(facilityId);
            if (facility == null)
            {
                _logger.LogWarning("Facility {Id} not found", facilityId);
                return false;
            }

            request.AssignedFacilityId = facilityId;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Assigned facility {FacilityName} to emergency request {RequestId}",
                facility.Name, emergencyRequestId);

            return true;
        }

        public async Task<bool> AssignFacilityToShelterAsync(int shelterRegistrationId, int facilityId)
        {
            var registration = await _dbContext.ShelterRegistrations.FindAsync(shelterRegistrationId);
            if (registration == null)
            {
                _logger.LogWarning("Shelter registration {Id} not found", shelterRegistrationId);
                return false;
            }

            var facility = await _dbContext.Facilities.FindAsync(facilityId);
            if (facility == null)
            {
                _logger.LogWarning("Facility {Id} not found", facilityId);
                return false;
            }

            // Update capacity if assigning to a shelter
            if (facility.CurrentOccupancy.HasValue)
            {
                facility.CurrentOccupancy += registration.FamilySize;
                facility.LastUpdatedAt = DateTime.UtcNow;
            }

            registration.AssignedFacilityId = facilityId;
            registration.ShelterName = facility.Name;
            registration.ShelterAddress = facility.Address;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Assigned facility {FacilityName} to shelter registration {RegistrationId}",
                facility.Name, shelterRegistrationId);

            return true;
        }

        public async Task<bool> AssignFacilityToEvacuationAsync(int evacuationRequestId, int facilityId)
        {
            var request = await _dbContext.EvacuationRequests.FindAsync(evacuationRequestId);
            if (request == null)
            {
                _logger.LogWarning("Evacuation request {Id} not found", evacuationRequestId);
                return false;
            }

            var facility = await _dbContext.Facilities.FindAsync(facilityId);
            if (facility == null)
            {
                _logger.LogWarning("Facility {Id} not found", facilityId);
                return false;
            }

            request.AssignedFacilityId = facilityId;
            request.DestinationLocation = facility.Address ?? facility.Name;
            request.DestinationLatitude = facility.Latitude;
            request.DestinationLongitude = facility.Longitude;
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Assigned facility {FacilityName} as destination for evacuation request {RequestId}",
                facility.Name, evacuationRequestId);

            return true;
        }

        public async Task<List<Facility>> GetSuitableFacilitiesAsync(string requestType, double? latitude = null, double? longitude = null)
        {
            FacilityType[] facilityTypes = requestType.ToLower() switch
            {
                "emergency" or "medical" => new[] { FacilityType.Hospital, FacilityType.Clinic },
                "fire" => new[] { FacilityType.FireStation },
                "violence" or "crime" => new[] { FacilityType.PoliceStation },
                "shelter" => new[] { FacilityType.Shelter },
                "evacuation" => new[] { FacilityType.EvacuationPoint, FacilityType.Shelter },
                _ => Enum.GetValues<FacilityType>()
            };

            var facilities = await _dbContext.Facilities
                .Where(f => f.IsOperational && facilityTypes.Contains(f.Type))
                .ToListAsync();

            // Sort by distance if coordinates provided
            if (latitude.HasValue && longitude.HasValue)
            {
                facilities = facilities
                    .OrderBy(f => CalculateDistance(latitude.Value, longitude.Value, f.Latitude, f.Longitude))
                    .ToList();
            }

            return facilities;
        }

        private Facility? FindNearest(List<Facility> facilities, double? latitude, double? longitude)
        {
            if (!latitude.HasValue || !longitude.HasValue)
            {
                // Return first available if no coordinates
                return facilities.FirstOrDefault();
            }

            return facilities
                .OrderBy(f => CalculateDistance(latitude.Value, longitude.Value, f.Latitude, f.Longitude))
                .FirstOrDefault();
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            // Haversine formula for distance between two coordinates
            const double R = 6371; // Earth's radius in kilometers

            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180;
    }
}
