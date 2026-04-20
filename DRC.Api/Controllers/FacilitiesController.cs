using DRC.Api.Data;
using DRC.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DRC.Api.Controllers;

/// <summary>
/// Public read-only access to the facility registry so citizens can locate
/// hospitals, shelters, police stations, etc. on an interactive map without
/// needing to authenticate.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FacilitiesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<FacilitiesController> _logger;

    public FacilitiesController(ApplicationDbContext db, ILogger<FacilitiesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>All operational facilities (lightweight projection for maps).</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] FacilityType? type = null)
    {
        var q = _db.Facilities.Where(f => f.IsOperational);
        if (type.HasValue) q = q.Where(f => f.Type == type.Value);

        var list = await q
            .OrderBy(f => f.Name)
            .Select(f => new FacilityDto(
                f.Id, f.Name, f.Type.ToString(), f.Address,
                f.Latitude, f.Longitude, f.Phone,
                f.Capacity, f.CurrentOccupancy, f.Is24Hours))
            .ToListAsync();

        return Ok(list);
    }

    /// <summary>
    /// Nearest facilities by great-circle distance to (lat,lng).
    /// Returns top N with a computed DistanceKm.
    /// </summary>
    [HttpGet("nearby")]
    public async Task<IActionResult> GetNearby(
        [FromQuery] double lat,
        [FromQuery] double lng,
        [FromQuery] double radiusKm = 25,
        [FromQuery] int limit = 20,
        [FromQuery] FacilityType? type = null)
    {
        if (lat < -90 || lat > 90 || lng < -180 || lng > 180)
            return BadRequest("Invalid coordinates.");

        var q = _db.Facilities.Where(f => f.IsOperational);
        if (type.HasValue) q = q.Where(f => f.Type == type.Value);

        // Pull operational facilities and compute haversine in-memory (dataset is small).
        var all = await q.ToListAsync();

        var results = all
            .Select(f => new FacilityNearbyDto(
                f.Id, f.Name, f.Type.ToString(), f.Address,
                f.Latitude, f.Longitude, f.Phone,
                f.Capacity, f.CurrentOccupancy, f.Is24Hours,
                Math.Round(Haversine(lat, lng, f.Latitude, f.Longitude), 2)))
            .Where(f => f.DistanceKm <= radiusKm)
            .OrderBy(f => f.DistanceKm)
            .Take(limit)
            .ToList();

        return Ok(results);
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371; // km
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;

    public record FacilityDto(
        int Id, string Name, string Type, string? Address,
        double Latitude, double Longitude, string? Phone,
        int? Capacity, int? CurrentOccupancy, bool Is24Hours);

    public record FacilityNearbyDto(
        int Id, string Name, string Type, string? Address,
        double Latitude, double Longitude, string? Phone,
        int? Capacity, int? CurrentOccupancy, bool Is24Hours,
        double DistanceKm);
}
