using DRC.Api.Interfaces;
using DRC.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace DRC.Api.Controllers;

/// <summary>
/// API controller for managing emergency alerts.
/// Used by authorities and emergency service providers to view and manage alerts.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AlertsController : ControllerBase
{
    private readonly IEmergencyAlertService _alertService;
    private readonly ILogger<AlertsController> _logger;

    public AlertsController(IEmergencyAlertService alertService, ILogger<AlertsController> logger)
    {
        _alertService = alertService;
        _logger = logger;
    }

    /// <summary>
    /// Get all active emergency alerts for the dashboard.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<EmergencyAlert>>> GetActiveAlerts()
    {
        try
        {
            var alerts = await _alertService.GetActiveAlertsAsync();
            return Ok(alerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving active alerts");
            return StatusCode(500, "Error retrieving alerts");
        }
    }

    /// <summary>
    /// Get alerts filtered by severity level.
    /// </summary>
    [HttpGet("severity/{severity}")]
    public async Task<ActionResult<IEnumerable<EmergencyAlert>>> GetAlertsBySeverity(EmergencySeverity severity)
    {
        try
        {
            var alerts = await _alertService.GetAlertsBySeverityAsync(severity);
            return Ok(alerts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving alerts by severity {Severity}", severity);
            return StatusCode(500, "Error retrieving alerts");
        }
    }

    /// <summary>
    /// Get a specific alert by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<EmergencyAlert>> GetAlert(string id)
    {
        try
        {
            var alert = await _alertService.GetAlertByIdAsync(id);
            if (alert == null)
            {
                return NotFound($"Alert with ID {id} not found");
            }
            return Ok(alert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving alert {AlertId}", id);
            return StatusCode(500, "Error retrieving alert");
        }
    }

    /// <summary>
    /// Update the status of an alert (for emergency responders).
    /// </summary>
    [HttpPut("{id}/status")]
    public async Task<ActionResult> UpdateAlertStatus(string id, [FromBody] UpdateAlertStatusRequest request)
    {
        try
        {
            var success = await _alertService.UpdateAlertStatusAsync(id, request.Status, request.Notes);
            if (!success)
            {
                return NotFound($"Alert with ID {id} not found");
            }
            
            _logger.LogInformation("Alert {AlertId} status updated to {Status} by responder", id, request.Status);
            return Ok(new { message = "Alert status updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating alert {AlertId} status", id);
            return StatusCode(500, "Error updating alert status");
        }
    }

    /// <summary>
    /// Manually create an emergency alert (for dispatch centers).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<EmergencyAlert>> CreateAlert([FromBody] CreateAlertRequest request)
    {
        try
        {
            var alert = await _alertService.CreateAlertAsync(
                request.Type,
                request.Severity,
                request.Location,
                request.Latitude,
                request.Longitude,
                request.Description,
                request.ContactPhone,
                request.ReportedBy
            );

            _logger.LogInformation("Manual alert created: {AlertId} - {Type} at {Location}", 
                alert.Id, alert.Type, alert.Location);

            // For critical alerts, immediately notify service providers
            if (alert.Severity == EmergencySeverity.Critical)
            {
                await _alertService.NotifyServiceProvidersAsync(alert);
            }

            return CreatedAtAction(nameof(GetAlert), new { id = alert.Id }, alert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating manual alert");
            return StatusCode(500, "Error creating alert");
        }
    }

    /// <summary>
    /// Get alert statistics for the dashboard.
    /// </summary>
    [HttpGet("statistics")]
    public async Task<ActionResult<AlertStatistics>> GetStatistics()
    {
        try
        {
            var alerts = await _alertService.GetActiveAlertsAsync();
            var alertsList = alerts.ToList();

            var statistics = new AlertStatistics
            {
                TotalActive = alertsList.Count,
                Critical = alertsList.Count(a => a.Severity == EmergencySeverity.Critical),
                High = alertsList.Count(a => a.Severity == EmergencySeverity.High),
                Medium = alertsList.Count(a => a.Severity == EmergencySeverity.Medium),
                Low = alertsList.Count(a => a.Severity == EmergencySeverity.Low),
                Pending = alertsList.Count(a => a.Status == AlertStatus.New),
                Dispatched = alertsList.Count(a => a.Status == AlertStatus.Dispatched),
                InProgress = alertsList.Count(a => a.Status == AlertStatus.OnScene),
                ByType = alertsList.GroupBy(a => a.Type)
                    .ToDictionary(g => g.Key.ToString(), g => g.Count())
            };

            return Ok(statistics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving alert statistics");
            return StatusCode(500, "Error retrieving statistics");
        }
    }
}

// Request DTOs
public class UpdateAlertStatusRequest
{
    public AlertStatus Status { get; set; }
    public string? Notes { get; set; }
}

public class CreateAlertRequest
{
    public EmergencyType Type { get; set; }
    public EmergencySeverity Severity { get; set; }
    public string Location { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public string ReportedBy { get; set; } = string.Empty;
}

public class AlertStatistics
{
    public int TotalActive { get; set; }
    public int Critical { get; set; }
    public int High { get; set; }
    public int Medium { get; set; }
    public int Low { get; set; }
    public int Pending { get; set; }
    public int Dispatched { get; set; }
    public int InProgress { get; set; }
    public Dictionary<string, int> ByType { get; set; } = new();
}
