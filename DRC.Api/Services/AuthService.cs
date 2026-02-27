using DRC.Api.Data;
using DRC.Api.Data.Entities;
using DRC.Api.Interfaces;
using DRC.Api.Models.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace DRC.Api.Services
{
    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            ApplicationDbContext context,
            IConfiguration configuration,
            ILogger<AuthService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
        {
            try
            {
                // Check if email already exists
                if (await _context.Users.AnyAsync(u => u.Email == request.Email))
                {
                    return new AuthResponse { Success = false, Message = "Email already registered" };
                }

                // Check if phone already exists
                if (await _context.Users.AnyAsync(u => u.Phone == request.Phone))
                {
                    return new AuthResponse { Success = false, Message = "Phone number already registered" };
                }

                var user = new User
                {
                    FullName = request.FullName,
                    Email = request.Email,
                    Phone = request.Phone,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                    Address = request.Address,
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                    Role = UserRole.User,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                var token = GenerateJwtToken(user);
                var expiry = DateTime.UtcNow.AddDays(7);

                return new AuthResponse
                {
                    Success = true,
                    Message = "Registration successful",
                    Token = token,
                    ExpiresAt = expiry,
                    User = MapToDto(user)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error registering user");
                return new AuthResponse { Success = false, Message = "Registration failed" };
            }
        }

        public async Task<AuthResponse> LoginAsync(LoginRequest request)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == request.EmailOrPhone || u.Phone == request.EmailOrPhone);

                if (user == null)
                {
                    return new AuthResponse { Success = false, Message = "Invalid credentials" };
                }

                if (!user.IsActive)
                {
                    return new AuthResponse { Success = false, Message = "Account is deactivated" };
                }

                if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                {
                    return new AuthResponse { Success = false, Message = "Invalid credentials" };
                }

                user.LastLoginAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                var token = GenerateJwtToken(user);
                var expiry = DateTime.UtcNow.AddDays(7);

                return new AuthResponse
                {
                    Success = true,
                    Message = "Login successful",
                    Token = token,
                    ExpiresAt = expiry,
                    User = MapToDto(user)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                return new AuthResponse { Success = false, Message = "Login failed" };
            }
        }

        public async Task<UserDto?> GetUserByIdAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            return user != null ? MapToDto(user) : null;
        }

        public async Task<AuthResponse> UpdateProfileAsync(int userId, UpdateProfileRequest request)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return new AuthResponse { Success = false, Message = "User not found" };
                }

                if (!string.IsNullOrEmpty(request.FullName))
                    user.FullName = request.FullName;
                if (!string.IsNullOrEmpty(request.Address))
                    user.Address = request.Address;
                if (request.Latitude.HasValue)
                    user.Latitude = request.Latitude;
                if (request.Longitude.HasValue)
                    user.Longitude = request.Longitude;

                await _context.SaveChangesAsync();

                return new AuthResponse
                {
                    Success = true,
                    Message = "Profile updated",
                    User = MapToDto(user)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating profile");
                return new AuthResponse { Success = false, Message = "Update failed" };
            }
        }

        public async Task<AuthResponse> ChangePasswordAsync(int userId, ChangePasswordRequest request)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return new AuthResponse { Success = false, Message = "User not found" };
                }

                if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
                {
                    return new AuthResponse { Success = false, Message = "Current password is incorrect" };
                }

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                await _context.SaveChangesAsync();

                return new AuthResponse { Success = true, Message = "Password changed successfully" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password");
                return new AuthResponse { Success = false, Message = "Password change failed" };
            }
        }

        public async Task<List<EmergencyContact>> GetEmergencyContactsAsync(int userId)
        {
            return await _context.EmergencyContacts
                .Where(c => c.UserId == userId)
                .OrderByDescending(c => c.IsPrimary)
                .ThenBy(c => c.FullName)
                .ToListAsync();
        }

        public async Task<EmergencyContact?> AddEmergencyContactAsync(int userId, AddEmergencyContactRequest request)
        {
            try
            {
                if (!Enum.TryParse<ContactRelationship>(request.Relationship, true, out var relationship))
                {
                    relationship = ContactRelationship.Other;
                }

                var contact = new EmergencyContact
                {
                    UserId = userId,
                    FullName = request.FullName,
                    Phone = request.Phone,
                    Email = request.Email,
                    Relationship = relationship,
                    IsPrimary = request.IsPrimary,
                    NotifyOnEmergency = request.NotifyOnEmergency,
                    NotifyOnEvacuation = request.NotifyOnEvacuation,
                    NotifyOnShelter = request.NotifyOnShelter,
                    CreatedAt = DateTime.UtcNow
                };

                // If this is primary, unset other primary contacts
                if (request.IsPrimary)
                {
                    var existingPrimary = await _context.EmergencyContacts
                        .Where(c => c.UserId == userId && c.IsPrimary)
                        .ToListAsync();
                    foreach (var c in existingPrimary)
                    {
                        c.IsPrimary = false;
                    }
                }

                _context.EmergencyContacts.Add(contact);
                await _context.SaveChangesAsync();

                return contact;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding emergency contact");
                return null;
            }
        }

        public async Task<bool> DeleteEmergencyContactAsync(int userId, int contactId)
        {
            var contact = await _context.EmergencyContacts
                .FirstOrDefaultAsync(c => c.Id == contactId && c.UserId == userId);

            if (contact == null) return false;

            _context.EmergencyContacts.Remove(contact);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<UserHistoryDto> GetUserHistoryAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return new UserHistoryDto();
            }

            var history = new UserHistoryDto();

            // Get emergency requests (by user ID or phone)
            var emergencyRequests = await _context.EmergencyRequests
                .Where(r => r.UserId == userId || r.UserPhone == user.Phone)
                .OrderByDescending(r => r.CreatedAt)
                .Take(20)
                .ToListAsync();

            history.EmergencyRequests = emergencyRequests.Select(r => new EmergencyRequestHistoryItem
            {
                Id = r.Id,
                Type = r.Type.ToString(),
                Severity = r.Severity.ToString(),
                Location = r.Location,
                Status = r.Status.ToString(),
                CreatedAt = r.CreatedAt
            }).ToList();

            // Get shelter registrations
            var shelterRegistrations = await _context.ShelterRegistrations
                .Where(r => r.UserId == userId || r.Phone == user.Phone)
                .OrderByDescending(r => r.CreatedAt)
                .Take(20)
                .ToListAsync();

            history.ShelterRegistrations = shelterRegistrations.Select(r => new ShelterRegistrationHistoryItem
            {
                Id = r.Id,
                FamilySize = r.FamilySize,
                ShelterName = r.ShelterName,
                Status = r.Status.ToString(),
                CreatedAt = r.CreatedAt
            }).ToList();

            // Get evacuation requests
            var evacuationRequests = await _context.EvacuationRequests
                .Where(r => r.UserId == userId || r.Phone == user.Phone)
                .OrderByDescending(r => r.CreatedAt)
                .Take(20)
                .ToListAsync();

            history.EvacuationRequests = evacuationRequests.Select(r => new EvacuationRequestHistoryItem
            {
                Id = r.Id,
                Location = r.PickupLocation,
                NumberOfPeople = r.NumberOfPeople,
                Priority = r.Priority.ToString(),
                Status = r.Status.ToString(),
                CreatedAt = r.CreatedAt
            }).ToList();

            return history;
        }

        public string GenerateJwtToken(User user)
        {
            var jwtKey = _configuration["Jwt:Key"] ?? "DisasterResponseCoordinatorSecretKey2024!@#$%";
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? "DRC.Api";
            var jwtAudience = _configuration["Jwt:Audience"] ?? "DRC.App";

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.MobilePhone, user.Phone),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: DateTime.UtcNow.AddDays(7),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static UserDto MapToDto(User user)
        {
            return new UserDto
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Phone = user.Phone,
                Role = user.Role.ToString(),
                Address = user.Address,
                Latitude = user.Latitude,
                Longitude = user.Longitude,
                IsActive = user.IsActive,
                CreatedAt = user.CreatedAt
            };
        }
    }
}
