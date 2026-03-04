using System.Net.Http.Json;
using Microsoft.JSInterop;

namespace DRC.App.Services
{
    public class UserClientService
    {
        private readonly HttpClient _httpClient;
        private readonly IJSRuntime _jsRuntime;
        private string? _authToken;
        private UserProfileDto? _currentUser;
        private bool _initialized = false;

        public UserClientService(HttpClient httpClient, IJSRuntime jsRuntime)
        {
            _httpClient = httpClient;
            _jsRuntime = jsRuntime;
        }

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            
            try
            {
                // Try to restore auth state from browser storage
                var token = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "drc_auth_token");
                var userJson = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", "drc_user");
                
                if (!string.IsNullOrEmpty(token))
                {
                    _authToken = token;
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    
                    if (!string.IsNullOrEmpty(userJson))
                    {
                        _currentUser = System.Text.Json.JsonSerializer.Deserialize<UserProfileDto>(userJson);
                    }
                }
            }
            catch
            {
                // JS interop may fail during prerendering
            }
            
            _initialized = true;
        }

        public void SetAuthToken(string token)
        {
            _authToken = token;
            _httpClient.DefaultRequestHeaders.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        private async Task SaveToStorageAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(_authToken))
                {
                    await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "drc_auth_token", _authToken);
                    if (_currentUser != null)
                    {
                        var userJson = System.Text.Json.JsonSerializer.Serialize(_currentUser);
                        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", "drc_user", userJson);
                    }
                }
            }
            catch
            {
                // Ignore storage errors
            }
        }

        private async Task ClearStorageAsync()
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "drc_auth_token");
                await _jsRuntime.InvokeVoidAsync("localStorage.removeItem", "drc_user");
            }
            catch
            {
                // Ignore storage errors
            }
        }

        public bool IsAuthenticated => !string.IsNullOrEmpty(_authToken);
        public string? AuthToken => _authToken;
        public UserProfileDto? CurrentUser => _currentUser;

        public async Task<AuthResult?> RegisterAsync(string fullName, string email, string phone, string password)
        {
            var response = await _httpClient.PostAsJsonAsync("/api/auth/register", new 
            { 
                fullName, 
                email, 
                phone, 
                password 
            });

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResult>();
                if (result != null)
                {
                    SetAuthToken(result.Token);
                    _currentUser = result.User;
                    await SaveToStorageAsync();
                }
                return result;
            }

            return null;
        }

        public async Task<AuthResult?> LoginAsync(string email, string password)
        {
            var response = await _httpClient.PostAsJsonAsync("/api/auth/login", new { emailOrPhone = email, password });
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AuthResult>();
                if (result != null)
                {
                    SetAuthToken(result.Token);
                    _currentUser = result.User;
                    await SaveToStorageAsync();
                }
                return result;
            }
            return null;
        }

        public async Task<UserProfileDto?> GetProfileAsync()
        {
            if (!IsAuthenticated) return null;
            
            try
            {
                var response = await _httpClient.GetAsync("/api/auth/profile");
                if (response.IsSuccessStatusCode)
                {
                    var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();
                    _currentUser = profile;
                    return profile;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting profile: {ex.Message}");
            }
            return null;
        }

        public async Task<bool> UpdateProfileAsync(string fullName, string phone)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync("/api/auth/profile", new { fullName, phone });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<List<EmergencyContactDto>?> GetEmergencyContactsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/auth/emergency-contacts");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<List<EmergencyContactDto>>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting emergency contacts: {ex.Message}");
            }
            return new List<EmergencyContactDto>();
        }

        public async Task<bool> AddEmergencyContactAsync(string name, string phone, string relationship, string? email = null, string? whatsAppNumber = null)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("/api/auth/emergency-contacts", new { 
                    fullName = name, 
                    phone,
                    email,
                    whatsAppNumber,
                    relationship,
                    notifyOnEmergency = true,
                    notifyOnEvacuation = true,
                    notifyOnShelter = true
                });
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<bool> DeleteEmergencyContactAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"/api/auth/emergency-contacts/{id}");
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public async Task<UserHistoryDto?> GetHistoryAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/auth/history");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<UserHistoryDto>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting history: {ex.Message}");
            }
            return null;
        }

        public async Task LogoutAsync()
        {
            _authToken = null;
            _currentUser = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
            await ClearStorageAsync();
        }

        // Sync method for backward compatibility
        public void Logout()
        {
            _authToken = null;
            _currentUser = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
            _ = ClearStorageAsync();
        }
    }

    public class AuthResult
    {
        public string Token { get; set; } = "";
        public UserProfileDto User { get; set; } = new();
    }

    public class UserProfileDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Role { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class EmergencyContactDto
    {
        public int Id { get; set; }
        public string FullName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string? Email { get; set; }
        public string? WhatsAppNumber { get; set; }
        public string Relationship { get; set; } = "";
    }

    // Alias for backward compatibility in the UI
    public static class EmergencyContactDtoExtensions
    {
        public static string Name(this EmergencyContactDto dto) => dto.FullName;
    }

    public class UserHistoryDto
    {
        public List<EmergencyRequestHistoryDto> EmergencyRequests { get; set; } = new();
        public List<ShelterRegistrationHistoryDto> ShelterRegistrations { get; set; } = new();
        public List<EvacuationRequestHistoryDto> EvacuationRequests { get; set; } = new();
    }

    public class EmergencyRequestHistoryDto
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Location { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class ShelterRegistrationHistoryDto
    {
        public int Id { get; set; }
        public int FamilySize { get; set; }
        public string? ShelterName { get; set; }
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class EvacuationRequestHistoryDto
    {
        public int Id { get; set; }
        public string Location { get; set; } = "";
        public int NumberOfPeople { get; set; }
        public string Priority { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
