using DRC.Api.Interfaces;
using DRC.Api.Models.Auth;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DRC.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _authService.RegisterAsync(request);

            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var result = await _authService.LoginAsync(request);

            if (!result.Success)
                return Unauthorized(result);

            return Ok(result);
        }

        // Temporary endpoint to reset admin password - REMOVE IN PRODUCTION
        [HttpPost("reset-admin")]
        public async Task<IActionResult> ResetAdminPassword([FromBody] AdminResetRequest request)
        {
            if (request.SecretKey != "drc-reset-2024")
                return Unauthorized(new { message = "Invalid secret key" });

            var result = await _authService.ResetAdminPasswordAsync(request.NewPassword);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var user = await _authService.GetUserByIdAsync(userId);
            if (user == null)
                return NotFound();

            return Ok(user);
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var result = await _authService.UpdateProfileAsync(userId, request);

            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var result = await _authService.ChangePasswordAsync(userId, request);

            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpGet("emergency-contacts")]
        public async Task<IActionResult> GetEmergencyContacts()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var contacts = await _authService.GetEmergencyContactsAsync(userId);
            return Ok(contacts);
        }

        [HttpPost("emergency-contacts")]
        public async Task<IActionResult> AddEmergencyContact([FromBody] AddEmergencyContactRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var contact = await _authService.AddEmergencyContactAsync(userId, request);
            if (contact == null)
                return BadRequest(new { message = "Failed to add contact" });

            return Ok(contact);
        }

        [HttpDelete("emergency-contacts/{contactId}")]
        public async Task<IActionResult> DeleteEmergencyContact(int contactId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var success = await _authService.DeleteEmergencyContactAsync(userId, contactId);
            if (!success)
                return NotFound();

            return Ok(new { message = "Contact deleted" });
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetUserHistory()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out var userId))
                return Unauthorized();

            var history = await _authService.GetUserHistoryAsync(userId);
            return Ok(history);
        }
    }
}
