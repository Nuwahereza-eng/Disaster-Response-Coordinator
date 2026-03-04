using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace DRC.Api.Models.Auth
{
    public class RegisterRequest
    {
        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Phone]
        public string Phone { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Address { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public class LoginRequest
    {
        [Required]
        [JsonPropertyName("emailOrPhone")]
        public string EmailOrPhone { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("password")]
        public string Password { get; set; } = string.Empty;
    }

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
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string? Address { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class UpdateProfileRequest
    {
        [MaxLength(100)]
        public string? FullName { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public class ChangePasswordRequest
    {
        [Required]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string NewPassword { get; set; } = string.Empty;
    }

    public class AddEmergencyContactRequest
    {
        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [Phone]
        public string Phone { get; set; } = string.Empty;

        [EmailAddress]
        public string? Email { get; set; }

        [Phone]
        public string? WhatsAppNumber { get; set; }

        [Required]
        public string Relationship { get; set; } = string.Empty;

        public bool IsPrimary { get; set; }
        public bool NotifyOnEmergency { get; set; } = true;
        public bool NotifyOnEvacuation { get; set; } = true;
        public bool NotifyOnShelter { get; set; } = true;
    }

    // Temporary model for admin password reset
    public class AdminResetRequest
    {
        [Required]
        public string SecretKey { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string NewPassword { get; set; } = string.Empty;
    }
}
