using System.Net.Http.Json;
using System.Text.Json;

namespace DRC.App.Services
{
    public class AdminClientService
    {
        private readonly HttpClient _httpClient;
        private string? _authToken;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public AdminClientService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public void SetAuthToken(string? token)
        {
            _authToken = token;
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            else
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
            }
        }

        public bool IsAuthenticated => !string.IsNullOrEmpty(_authToken);

        // Auth endpoints
        public async Task<AuthResponse?> LoginAsync(string email, string password)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new { emailOrPhone = email, password });
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
                    if (result?.Token != null)
                    {
                        SetAuthToken(result.Token);
                    }
                    return result;
                }
            }
            catch { }
            return null;
        }

        // Dashboard
        public async Task<DashboardStats?> GetDashboardAsync()
        {
            try
            {
                var json = await _httpClient.GetStringAsync("/api/admin/dashboard");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                return new DashboardStats
                {
                    TotalUsers = root.GetProperty("users").GetProperty("total").GetInt32(),
                    TotalEmergencyRequests = root.GetProperty("emergencyRequests").GetProperty("total").GetInt32(),
                    PendingEmergencyRequests = root.GetProperty("emergencyRequests").GetProperty("pending").GetInt32(),
                    TotalShelterRegistrations = root.GetProperty("shelterRegistrations").GetProperty("total").GetInt32(),
                    PendingShelterRegistrations = root.GetProperty("shelterRegistrations").GetProperty("pending").GetInt32(),
                    TotalEvacuationRequests = root.GetProperty("evacuationRequests").GetProperty("total").GetInt32(),
                    PendingEvacuationRequests = root.GetProperty("evacuationRequests").GetProperty("pending").GetInt32(),
                    TotalFacilities = root.GetProperty("facilities").GetProperty("total").GetInt32(),
                    TotalNotifications = root.GetProperty("notifications").GetProperty("total").GetInt32()
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Dashboard error: {ex.Message}");
                return new DashboardStats();
            }
        }

        // Users
        public async Task<List<UserDto>?> GetUsersAsync()
        {
            try
            {
                var json = await _httpClient.GetStringAsync("/api/admin/users");
                using var doc = JsonDocument.Parse(json);
                var usersArray = doc.RootElement.GetProperty("users");
                return JsonSerializer.Deserialize<List<UserDto>>(usersArray.GetRawText(), _jsonOptions) ?? new List<UserDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Users error: {ex.Message}");
                return new List<UserDto>();
            }
        }

        // Emergency Requests
        public async Task<List<EmergencyRequestDto>?> GetEmergencyRequestsAsync()
        {
            try
            {
                var json = await _httpClient.GetStringAsync("/api/admin/emergency-requests");
                using var doc = JsonDocument.Parse(json);
                var requestsArray = doc.RootElement.GetProperty("requests");
                return JsonSerializer.Deserialize<List<EmergencyRequestDto>>(requestsArray.GetRawText(), _jsonOptions) ?? new List<EmergencyRequestDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Emergency requests error: {ex.Message}");
                return new List<EmergencyRequestDto>();
            }
        }

        public async Task<bool> UpdateEmergencyStatusAsync(int id, string status)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync($"/api/admin/emergency-requests/{id}/status", new { status });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // Shelter Registrations
        public async Task<List<ShelterRegistrationDto>?> GetShelterRegistrationsAsync()
        {
            try
            {
                var json = await _httpClient.GetStringAsync("/api/admin/shelter-registrations");
                using var doc = JsonDocument.Parse(json);
                var registrationsArray = doc.RootElement.GetProperty("registrations");
                return JsonSerializer.Deserialize<List<ShelterRegistrationDto>>(registrationsArray.GetRawText(), _jsonOptions) ?? new List<ShelterRegistrationDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Shelter registrations error: {ex.Message}");
                return new List<ShelterRegistrationDto>();
            }
        }

        public async Task<bool> CheckInShelterAsync(int id)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync($"/api/admin/shelter-registrations/{id}/checkin", new { });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // Evacuation Requests
        public async Task<List<EvacuationRequestDto>?> GetEvacuationRequestsAsync()
        {
            try
            {
                var json = await _httpClient.GetStringAsync("/api/admin/evacuation-requests");
                using var doc = JsonDocument.Parse(json);
                var requestsArray = doc.RootElement.GetProperty("requests");
                return JsonSerializer.Deserialize<List<EvacuationRequestDto>>(requestsArray.GetRawText(), _jsonOptions) ?? new List<EvacuationRequestDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Evacuation requests error: {ex.Message}");
                return new List<EvacuationRequestDto>();
            }
        }

        public async Task<bool> AssignEvacuationVehicleAsync(int id, string vehicle, string? driver, string? phone)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync($"/api/admin/evacuation-requests/{id}/assign", 
                    new { assignedVehicle = vehicle, driverName = driver, driverPhone = phone });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<bool> UpdateEvacuationStatusAsync(int id, string status)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync($"/api/admin/evacuation-requests/{id}/status", 
                    new { status = status });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // Facility Assignment
        public async Task<bool> AssignFacilityToEmergencyAsync(int requestId, int facilityId)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    $"/api/admin/emergency-requests/{requestId}/assign-facility", 
                    new { facilityId });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<bool> AssignFacilityToShelterAsync(int registrationId, int facilityId)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    $"/api/admin/shelter-registrations/{registrationId}/assign-facility", 
                    new { facilityId });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<bool> AssignFacilityToEvacuationAsync(int requestId, int facilityId)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    $"/api/admin/evacuation-requests/{requestId}/assign-facility", 
                    new { facilityId });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<List<FacilityDto>?> GetSuitableFacilitiesAsync(string requestType)
        {
            try
            {
                var json = await _httpClient.GetStringAsync($"/api/admin/facilities/suitable?requestType={requestType}");
                using var doc = JsonDocument.Parse(json);
                var facilitiesArray = doc.RootElement.GetProperty("facilities");
                return JsonSerializer.Deserialize<List<FacilityDto>>(facilitiesArray.GetRawText(), _jsonOptions) ?? new List<FacilityDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Get suitable facilities error: {ex.Message}");
                return new List<FacilityDto>();
            }
        }

        // Facilities
        public async Task<List<FacilityDto>?> GetFacilitiesAsync()
        {
            try
            {
                var json = await _httpClient.GetStringAsync("/api/admin/facilities");
                using var doc = JsonDocument.Parse(json);
                var facilitiesArray = doc.RootElement.GetProperty("facilities");
                return JsonSerializer.Deserialize<List<FacilityDto>>(facilitiesArray.GetRawText(), _jsonOptions) ?? new List<FacilityDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Facilities error: {ex.Message}");
                return new List<FacilityDto>();
            }
        }

        public async Task<bool> CreateFacilityAsync(object facilityData)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/api/admin/facilities", facilityData);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Create facility error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> UpdateFacilityAsync(int id, object facilityData)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync($"/api/admin/facilities/{id}", facilityData);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Update facility error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> DeleteFacilityAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"/api/admin/facilities/{id}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Delete facility error: {ex.Message}");
                return false;
            }
        }

        // Notifications
        public async Task<List<NotificationDto>?> GetNotificationsAsync()
        {
            try
            {
                var json = await _httpClient.GetStringAsync("/api/admin/notifications");
                using var doc = JsonDocument.Parse(json);
                var notificationsArray = doc.RootElement.GetProperty("notifications");
                return JsonSerializer.Deserialize<List<NotificationDto>>(notificationsArray.GetRawText(), _jsonOptions) ?? new List<NotificationDto>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Notifications error: {ex.Message}");
                return new List<NotificationDto>();
            }
        }

        // Change Password
        public async Task<ChangePasswordResponse> ChangePasswordAsync(string currentPassword, string newPassword)
        {
            try
            {
                var request = new { CurrentPassword = currentPassword, NewPassword = newPassword };
                var response = await _httpClient.PostAsJsonAsync("/api/Auth/change-password", request);
                var content = await response.Content.ReadAsStringAsync();
                
                if (response.IsSuccessStatusCode)
                {
                    return new ChangePasswordResponse { Success = true, Message = "Password changed successfully!" };
                }
                
                // Try to parse error message
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var message = doc.RootElement.TryGetProperty("message", out var msgProp) 
                        ? msgProp.GetString() 
                        : "Failed to change password";
                    return new ChangePasswordResponse { Success = false, Message = message };
                }
                catch
                {
                    return new ChangePasswordResponse { Success = false, Message = "Failed to change password" };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Change password error: {ex.Message}");
                return new ChangePasswordResponse { Success = false, Message = "Error connecting to server" };
            }
        }
    }

    public class ChangePasswordResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
    }

    // DTOs for the client
    public class AuthResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Token { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public UserDto? User { get; set; }
    }

    public class UserDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Role { get; set; } = "";
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class DashboardStats
    {
        public int TotalUsers { get; set; }
        public int TotalEmergencyRequests { get; set; }
        public int PendingEmergencyRequests { get; set; }
        public int TotalShelterRegistrations { get; set; }
        public int PendingShelterRegistrations { get; set; }
        public int TotalEvacuationRequests { get; set; }
        public int PendingEvacuationRequests { get; set; }
        public int TotalFacilities { get; set; }
        public int TotalNotifications { get; set; }
    }

    public class EmergencyRequestDto
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Description { get; set; } = "";
        public string? Location { get; set; }
        public string Status { get; set; } = "";
        public string? UserPhone { get; set; }
        public bool AmbulanceDispatched { get; set; }
        public bool FireBrigadeDispatched { get; set; }
        public bool PoliceDispatched { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? AssignedFacilityId { get; set; }
        public string? AssignedFacilityName { get; set; }
        public string? AssignedFacilityType { get; set; }
    }

    public class ShelterRegistrationDto
    {
        public int Id { get; set; }
        public string? FullName { get; set; }
        public string? Phone { get; set; }
        public int FamilySize { get; set; }
        public int Adults { get; set; }
        public int Children { get; set; }
        public int Elderly { get; set; }
        public string? SpecialNeeds { get; set; }
        public string Status { get; set; } = "";
        public string? ShelterName { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? AssignedFacilityId { get; set; }
        public string? AssignedFacilityName { get; set; }
        public int? AssignedFacilityCapacity { get; set; }
        public int? AssignedFacilityOccupancy { get; set; }
    }

    public class EvacuationRequestDto
    {
        public int Id { get; set; }
        public string? FullName { get; set; }
        public string? Phone { get; set; }
        public int NumberOfPeople { get; set; }
        public bool HasElderly { get; set; }
        public bool HasChildren { get; set; }
        public bool HasDisabled { get; set; }
        public string PickupLocation { get; set; } = "";
        public string Priority { get; set; } = "";
        public string Status { get; set; } = "";
        public string? AssignedVehicle { get; set; }
        public string? DriverName { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? AssignedFacilityId { get; set; }
        public string? AssignedFacilityName { get; set; }
        public string? AssignedFacilityAddress { get; set; }
    }

    public class FacilityDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Description { get; set; }
        public string? ServicesOffered { get; set; }
        public string? OperatingHours { get; set; }
        public bool Is24Hours { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int? Capacity { get; set; }
        public int? CurrentOccupancy { get; set; }
        public bool IsOperational { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
    }

    public class NotificationDto
    {
        public int Id { get; set; }
        public string RecipientPhone { get; set; } = "";
        public string? RecipientName { get; set; }
        public string Type { get; set; } = "";
        public string Channel { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Message { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }
}
