using DRC.Api.Models.Auth;
using DRC.Api.Data.Entities;

namespace DRC.Api.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponse> RegisterAsync(RegisterRequest request);
        Task<AuthResponse> LoginAsync(LoginRequest request);
        Task<UserDto?> GetUserByIdAsync(int userId);
        Task<AuthResponse> UpdateProfileAsync(int userId, UpdateProfileRequest request);
        Task<AuthResponse> ChangePasswordAsync(int userId, ChangePasswordRequest request);
        Task<List<EmergencyContact>> GetEmergencyContactsAsync(int userId);
        Task<EmergencyContact?> AddEmergencyContactAsync(int userId, AddEmergencyContactRequest request);
        Task<bool> DeleteEmergencyContactAsync(int userId, int contactId);
        Task<UserHistoryDto> GetUserHistoryAsync(int userId);
        string GenerateJwtToken(User user);
    }

    public class UserHistoryDto
    {
        public List<EmergencyRequestHistoryItem> EmergencyRequests { get; set; } = new();
        public List<ShelterRegistrationHistoryItem> ShelterRegistrations { get; set; } = new();
        public List<EvacuationRequestHistoryItem> EvacuationRequests { get; set; } = new();
    }

    public class EmergencyRequestHistoryItem
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Location { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class ShelterRegistrationHistoryItem
    {
        public int Id { get; set; }
        public int FamilySize { get; set; }
        public string? ShelterName { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class EvacuationRequestHistoryItem
    {
        public int Id { get; set; }
        public string Location { get; set; } = "";
        public int NumberOfPeople { get; set; }
        public string Priority { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
