using DRC.Api.Data;
using DRC.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DRC.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")]
    public class AdminController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AdminController> _logger;

        public AdminController(ApplicationDbContext context, ILogger<AdminController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ========== DASHBOARD ==========

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            var now = DateTime.UtcNow;
            var today = now.Date;
            var thisWeek = today.AddDays(-7);
            var thisMonth = today.AddDays(-30);

            var stats = new
            {
                Users = new
                {
                    Total = await _context.Users.CountAsync(),
                    Active = await _context.Users.CountAsync(u => u.IsActive),
                    RegisteredToday = await _context.Users.CountAsync(u => u.CreatedAt >= today),
                    RegisteredThisWeek = await _context.Users.CountAsync(u => u.CreatedAt >= thisWeek)
                },
                EmergencyRequests = new
                {
                    Total = await _context.EmergencyRequests.CountAsync(),
                    Pending = await _context.EmergencyRequests.CountAsync(r => r.Status == RequestStatus.Pending),
                    InProgress = await _context.EmergencyRequests.CountAsync(r => r.Status == RequestStatus.InProgress || r.Status == RequestStatus.Dispatched),
                    Resolved = await _context.EmergencyRequests.CountAsync(r => r.Status == RequestStatus.Resolved),
                    Today = await _context.EmergencyRequests.CountAsync(r => r.CreatedAt >= today),
                    ThisWeek = await _context.EmergencyRequests.CountAsync(r => r.CreatedAt >= thisWeek),
                    BySeverity = new
                    {
                        Critical = await _context.EmergencyRequests.CountAsync(r => r.Severity == EmergencySeverity.Critical && r.Status != RequestStatus.Resolved),
                        High = await _context.EmergencyRequests.CountAsync(r => r.Severity == EmergencySeverity.High && r.Status != RequestStatus.Resolved),
                        Medium = await _context.EmergencyRequests.CountAsync(r => r.Severity == EmergencySeverity.Medium && r.Status != RequestStatus.Resolved),
                        Low = await _context.EmergencyRequests.CountAsync(r => r.Severity == EmergencySeverity.Low && r.Status != RequestStatus.Resolved)
                    }
                },
                ShelterRegistrations = new
                {
                    Total = await _context.ShelterRegistrations.CountAsync(),
                    Pending = await _context.ShelterRegistrations.CountAsync(r => r.Status == RegistrationStatus.Pending),
                    CheckedIn = await _context.ShelterRegistrations.CountAsync(r => r.Status == RegistrationStatus.CheckedIn),
                    TotalPeopleRegistered = await _context.ShelterRegistrations.Where(r => r.Status == RegistrationStatus.Pending || r.Status == RegistrationStatus.Approved || r.Status == RegistrationStatus.CheckedIn).SumAsync(r => r.FamilySize),
                    Today = await _context.ShelterRegistrations.CountAsync(r => r.CreatedAt >= today)
                },
                EvacuationRequests = new
                {
                    Total = await _context.EvacuationRequests.CountAsync(),
                    Pending = await _context.EvacuationRequests.CountAsync(r => r.Status == EvacuationStatus.Requested || r.Status == EvacuationStatus.Acknowledged),
                    InProgress = await _context.EvacuationRequests.CountAsync(r => r.Status == EvacuationStatus.VehicleDispatched || r.Status == EvacuationStatus.EnRoute || r.Status == EvacuationStatus.InTransit),
                    Completed = await _context.EvacuationRequests.CountAsync(r => r.Status == EvacuationStatus.Completed),
                    TotalPeopleToEvacuate = await _context.EvacuationRequests.Where(r => r.Status != EvacuationStatus.Completed && r.Status != EvacuationStatus.Cancelled).SumAsync(r => r.NumberOfPeople),
                    Today = await _context.EvacuationRequests.CountAsync(r => r.CreatedAt >= today)
                },
                Notifications = new
                {
                    Total = await _context.AlertNotifications.CountAsync(),
                    Sent = await _context.AlertNotifications.CountAsync(n => n.Status == NotificationStatus.Sent || n.Status == NotificationStatus.Delivered),
                    Failed = await _context.AlertNotifications.CountAsync(n => n.Status == NotificationStatus.Failed),
                    Today = await _context.AlertNotifications.CountAsync(n => n.CreatedAt >= today)
                },
                Facilities = new
                {
                    Total = await _context.Facilities.CountAsync(),
                    Operational = await _context.Facilities.CountAsync(f => f.IsOperational),
                    Shelters = await _context.Facilities.CountAsync(f => f.Type == FacilityType.Shelter),
                    Hospitals = await _context.Facilities.CountAsync(f => f.Type == FacilityType.Hospital)
                }
            };

            return Ok(stats);
        }

        // ========== USERS ==========

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, [FromQuery] string? role = null)
        {
            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(u => u.FullName.Contains(search) || u.Email.Contains(search) || u.Phone.Contains(search));
            }

            if (!string.IsNullOrEmpty(role) && Enum.TryParse<UserRole>(role, true, out var userRole))
            {
                query = query.Where(u => u.Role == userRole);
            }

            var total = await query.CountAsync();
            var users = await query
                .OrderByDescending(u => u.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new
                {
                    u.Id,
                    u.FullName,
                    u.Email,
                    u.Phone,
                    Role = u.Role.ToString(),
                    u.IsActive,
                    u.CreatedAt,
                    u.LastLoginAt,
                    EmergencyRequestsCount = u.EmergencyRequests.Count,
                    ShelterRegistrationsCount = u.ShelterRegistrations.Count,
                    EvacuationRequestsCount = u.EvacuationRequests.Count
                })
                .ToListAsync();

            return Ok(new { total, page, pageSize, users });
        }

        [HttpPut("users/{id}/role")]
        public async Task<IActionResult> UpdateUserRole(int id, [FromBody] UpdateRoleRequest request)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            if (Enum.TryParse<UserRole>(request.Role, true, out var role))
            {
                user.Role = role;
                await _context.SaveChangesAsync();
                return Ok(new { message = "Role updated" });
            }

            return BadRequest(new { message = "Invalid role" });
        }

        [HttpPut("users/{id}/status")]
        public async Task<IActionResult> UpdateUserStatus(int id, [FromBody] UpdateStatusRequest request)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();

            user.IsActive = request.IsActive;
            await _context.SaveChangesAsync();
            return Ok(new { message = "Status updated" });
        }

        // ========== EMERGENCY REQUESTS ==========

        [HttpGet("emergency-requests")]
        public async Task<IActionResult> GetEmergencyRequests(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null,
            [FromQuery] string? severity = null,
            [FromQuery] string? type = null)
        {
            var query = _context.EmergencyRequests.Include(r => r.User).AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<RequestStatus>(status, true, out var requestStatus))
            {
                query = query.Where(r => r.Status == requestStatus);
            }

            if (!string.IsNullOrEmpty(severity) && Enum.TryParse<EmergencySeverity>(severity, true, out var sev))
            {
                query = query.Where(r => r.Severity == sev);
            }

            if (!string.IsNullOrEmpty(type) && Enum.TryParse<EmergencyType>(type, true, out var emergencyType))
            {
                query = query.Where(r => r.Type == emergencyType);
            }

            var total = await query.CountAsync();
            var requests = await query
                .OrderByDescending(r => r.Severity)
                .ThenByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    r.Id,
                    r.RequestGuid,
                    UserId = r.UserId,
                    UserName = r.User != null ? r.User.FullName : "Anonymous",
                    UserPhone = r.UserPhone ?? (r.User != null ? r.User.Phone : null),
                    Type = r.Type.ToString(),
                    Severity = r.Severity.ToString(),
                    r.Description,
                    r.Location,
                    r.Latitude,
                    r.Longitude,
                    Status = r.Status.ToString(),
                    r.AssignedResponder,
                    r.ResponseNotes,
                    r.AmbulanceDispatched,
                    r.FireBrigadeDispatched,
                    r.PoliceDispatched,
                    r.CreatedAt,
                    r.AcknowledgedAt,
                    r.ResolvedAt
                })
                .ToListAsync();

            return Ok(new { total, page, pageSize, requests });
        }

        [HttpPut("emergency-requests/{id}/status")]
        public async Task<IActionResult> UpdateEmergencyRequestStatus(int id, [FromBody] UpdateEmergencyStatusRequest request)
        {
            var emergencyRequest = await _context.EmergencyRequests.FindAsync(id);
            if (emergencyRequest == null) return NotFound();

            if (Enum.TryParse<RequestStatus>(request.Status, true, out var status))
            {
                emergencyRequest.Status = status;
                emergencyRequest.AssignedResponder = request.AssignedResponder;
                emergencyRequest.ResponseNotes = request.ResponseNotes;

                if (status == RequestStatus.Acknowledged && !emergencyRequest.AcknowledgedAt.HasValue)
                    emergencyRequest.AcknowledgedAt = DateTime.UtcNow;

                if (status == RequestStatus.Resolved && !emergencyRequest.ResolvedAt.HasValue)
                    emergencyRequest.ResolvedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Ok(new { message = "Status updated" });
            }

            return BadRequest(new { message = "Invalid status" });
        }

        // ========== SHELTER REGISTRATIONS ==========

        [HttpGet("shelter-registrations")]
        public async Task<IActionResult> GetShelterRegistrations(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null)
        {
            var query = _context.ShelterRegistrations.Include(r => r.User).AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<RegistrationStatus>(status, true, out var regStatus))
            {
                query = query.Where(r => r.Status == regStatus);
            }

            var total = await query.CountAsync();
            var registrations = await query
                .OrderByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    r.Id,
                    r.RegistrationGuid,
                    r.UserId,
                    UserName = r.User != null ? r.User.FullName : r.FullName,
                    Phone = r.Phone ?? (r.User != null ? r.User.Phone : null),
                    r.FamilySize,
                    r.Adults,
                    r.Children,
                    r.Elderly,
                    r.SpecialNeeds,
                    r.MedicalConditions,
                    r.ShelterName,
                    r.ShelterAddress,
                    Status = r.Status.ToString(),
                    r.CreatedAt,
                    r.CheckInAt,
                    r.CheckOutAt,
                    r.Notes
                })
                .ToListAsync();

            return Ok(new { total, page, pageSize, registrations });
        }

        [HttpPut("shelter-registrations/{id}/status")]
        public async Task<IActionResult> UpdateShelterRegistrationStatus(int id, [FromBody] UpdateShelterStatusRequest request)
        {
            var registration = await _context.ShelterRegistrations.FindAsync(id);
            if (registration == null) return NotFound();

            if (Enum.TryParse<RegistrationStatus>(request.Status, true, out var status))
            {
                registration.Status = status;
                registration.ShelterName = request.ShelterName ?? registration.ShelterName;
                registration.ShelterAddress = request.ShelterAddress ?? registration.ShelterAddress;
                registration.Notes = request.Notes ?? registration.Notes;

                if (status == RegistrationStatus.CheckedIn && !registration.CheckInAt.HasValue)
                    registration.CheckInAt = DateTime.UtcNow;

                if (status == RegistrationStatus.CheckedOut && !registration.CheckOutAt.HasValue)
                    registration.CheckOutAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Ok(new { message = "Status updated" });
            }

            return BadRequest(new { message = "Invalid status" });
        }

        // ========== EVACUATION REQUESTS ==========

        [HttpGet("evacuation-requests")]
        public async Task<IActionResult> GetEvacuationRequests(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null,
            [FromQuery] string? priority = null)
        {
            var query = _context.EvacuationRequests.Include(r => r.User).AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<EvacuationStatus>(status, true, out var evacStatus))
            {
                query = query.Where(r => r.Status == evacStatus);
            }

            if (!string.IsNullOrEmpty(priority) && Enum.TryParse<EvacuationPriority>(priority, true, out var prio))
            {
                query = query.Where(r => r.Priority == prio);
            }

            var total = await query.CountAsync();
            var requests = await query
                .OrderByDescending(r => r.Priority)
                .ThenByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(r => new
                {
                    r.Id,
                    r.RequestGuid,
                    r.UserId,
                    UserName = r.User != null ? r.User.FullName : r.FullName,
                    Phone = r.Phone ?? (r.User != null ? r.User.Phone : null),
                    r.NumberOfPeople,
                    r.HasElderly,
                    r.HasChildren,
                    r.HasDisabled,
                    r.NeedsMedicalAssistance,
                    r.SpecialRequirements,
                    r.PickupLocation,
                    r.PickupLatitude,
                    r.PickupLongitude,
                    r.DestinationLocation,
                    Status = r.Status.ToString(),
                    Priority = r.Priority.ToString(),
                    r.AssignedVehicle,
                    r.DriverName,
                    r.DriverPhone,
                    r.EstimatedArrival,
                    r.ActualArrival,
                    r.CreatedAt,
                    r.CompletedAt,
                    r.Notes
                })
                .ToListAsync();

            return Ok(new { total, page, pageSize, requests });
        }

        [HttpPut("evacuation-requests/{id}/status")]
        public async Task<IActionResult> UpdateEvacuationRequestStatus(int id, [FromBody] UpdateEvacuationStatusRequest request)
        {
            var evacuationRequest = await _context.EvacuationRequests.FindAsync(id);
            if (evacuationRequest == null) return NotFound();

            if (Enum.TryParse<EvacuationStatus>(request.Status, true, out var status))
            {
                evacuationRequest.Status = status;
                evacuationRequest.AssignedVehicle = request.AssignedVehicle ?? evacuationRequest.AssignedVehicle;
                evacuationRequest.DriverName = request.DriverName ?? evacuationRequest.DriverName;
                evacuationRequest.DriverPhone = request.DriverPhone ?? evacuationRequest.DriverPhone;
                evacuationRequest.EstimatedArrival = request.EstimatedArrival ?? evacuationRequest.EstimatedArrival;
                evacuationRequest.Notes = request.Notes ?? evacuationRequest.Notes;

                if (status == EvacuationStatus.Arrived && !evacuationRequest.ActualArrival.HasValue)
                    evacuationRequest.ActualArrival = DateTime.UtcNow;

                if (status == EvacuationStatus.Completed && !evacuationRequest.CompletedAt.HasValue)
                    evacuationRequest.CompletedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Ok(new { message = "Status updated" });
            }

            return BadRequest(new { message = "Invalid status" });
        }

        // ========== FACILITIES ==========

        [HttpGet("facilities")]
        public async Task<IActionResult> GetFacilities([FromQuery] string? type = null)
        {
            var query = _context.Facilities.AsQueryable();

            if (!string.IsNullOrEmpty(type) && Enum.TryParse<FacilityType>(type, true, out var facilityType))
            {
                query = query.Where(f => f.Type == facilityType);
            }

            var facilities = await query.OrderBy(f => f.Name).ToListAsync();
            return Ok(facilities);
        }

        [HttpPost("facilities")]
        public async Task<IActionResult> CreateFacility([FromBody] CreateFacilityRequest request)
        {
            if (!Enum.TryParse<FacilityType>(request.Type, true, out var facilityType))
                return BadRequest(new { message = "Invalid facility type" });

            var facility = new Facility
            {
                Name = request.Name,
                Type = facilityType,
                Address = request.Address,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Phone = request.Phone,
                Description = request.Description,
                Capacity = request.Capacity,
                CurrentOccupancy = 0,
                IsOperational = request.IsOperational,
                Is24Hours = request.Is24Hours,
                OperatingHours = request.OperatingHours,
                ServicesOffered = request.ServicesOffered,
                CreatedAt = DateTime.UtcNow
            };

            _context.Facilities.Add(facility);
            await _context.SaveChangesAsync();

            return Ok(facility);
        }

        [HttpPut("facilities/{id}")]
        public async Task<IActionResult> UpdateFacility(int id, [FromBody] UpdateFacilityRequest request)
        {
            var facility = await _context.Facilities.FindAsync(id);
            if (facility == null) return NotFound();

            if (!string.IsNullOrEmpty(request.Name)) facility.Name = request.Name;
            if (!string.IsNullOrEmpty(request.Address)) facility.Address = request.Address;
            if (request.Latitude.HasValue) facility.Latitude = request.Latitude.Value;
            if (request.Longitude.HasValue) facility.Longitude = request.Longitude.Value;
            if (!string.IsNullOrEmpty(request.Phone)) facility.Phone = request.Phone;
            if (!string.IsNullOrEmpty(request.Description)) facility.Description = request.Description;
            if (request.Capacity.HasValue) facility.Capacity = request.Capacity;
            if (request.CurrentOccupancy.HasValue) facility.CurrentOccupancy = request.CurrentOccupancy;
            if (request.IsOperational.HasValue) facility.IsOperational = request.IsOperational.Value;
            facility.LastUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(facility);
        }

        [HttpDelete("facilities/{id}")]
        public async Task<IActionResult> DeleteFacility(int id)
        {
            var facility = await _context.Facilities.FindAsync(id);
            if (facility == null) return NotFound();

            _context.Facilities.Remove(facility);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Facility deleted" });
        }

        // ========== NOTIFICATIONS ==========

        [HttpGet("notifications")]
        public async Task<IActionResult> GetNotifications(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null)
        {
            var query = _context.AlertNotifications.AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<NotificationStatus>(status, true, out var notifStatus))
            {
                query = query.Where(n => n.Status == notifStatus);
            }

            var total = await query.CountAsync();
            var notifications = await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(n => new
                {
                    n.Id,
                    n.RecipientPhone,
                    n.RecipientEmail,
                    n.RecipientName,
                    Type = n.Type.ToString(),
                    Channel = n.Channel.ToString(),
                    n.Subject,
                    n.Message,
                    Status = n.Status.ToString(),
                    n.ErrorMessage,
                    n.CreatedAt,
                    n.SentAt,
                    n.DeliveredAt
                })
                .ToListAsync();

            return Ok(new { total, page, pageSize, notifications });
        }
    }

    // Request DTOs for Admin endpoints
    public class UpdateRoleRequest
    {
        public string Role { get; set; } = string.Empty;
    }

    public class UpdateStatusRequest
    {
        public bool IsActive { get; set; }
    }

    public class UpdateEmergencyStatusRequest
    {
        public string Status { get; set; } = string.Empty;
        public string? AssignedResponder { get; set; }
        public string? ResponseNotes { get; set; }
    }

    public class UpdateShelterStatusRequest
    {
        public string Status { get; set; } = string.Empty;
        public string? ShelterName { get; set; }
        public string? ShelterAddress { get; set; }
        public string? Notes { get; set; }
    }

    public class UpdateEvacuationStatusRequest
    {
        public string Status { get; set; } = string.Empty;
        public string? AssignedVehicle { get; set; }
        public string? DriverName { get; set; }
        public string? DriverPhone { get; set; }
        public DateTime? EstimatedArrival { get; set; }
        public string? Notes { get; set; }
    }

    public class CreateFacilityRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Address { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? Phone { get; set; }
        public string? Description { get; set; }
        public int? Capacity { get; set; }
        public bool IsOperational { get; set; } = true;
        public bool Is24Hours { get; set; }
        public string? OperatingHours { get; set; }
        public string? ServicesOffered { get; set; }
    }

    public class UpdateFacilityRequest
    {
        public string? Name { get; set; }
        public string? Address { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? Phone { get; set; }
        public string? Description { get; set; }
        public int? Capacity { get; set; }
        public int? CurrentOccupancy { get; set; }
        public bool? IsOperational { get; set; }
    }
}
