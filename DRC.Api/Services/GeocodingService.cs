using DRC.Api.Interfaces;
using Newtonsoft.Json;

namespace DRC.Api.Services
{
    public class GeocodingService : IGeocodingService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeocodingService> _logger;

        public GeocodingService(HttpClient httpClient, IConfiguration configuration, ILogger<GeocodingService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("UgandaDisasterResponse/1.0");
        }

        public async Task<(double Latitude, double Longitude)> GetCoordinatesByPostalCodeAsync(string location)
        {
            // Using OpenStreetMap Nominatim API for Uganda locations
            var searchQuery = Uri.EscapeDataString(location + ", Uganda");
            var url = $"?format=json&q={searchQuery}&countrycodes=ug&limit=1";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();

            var data = JsonConvert.DeserializeObject<dynamic>(json);
            if (data != null && data.Count > 0)
            {
                double latitude = double.Parse((string)data[0].lat, System.Globalization.CultureInfo.InvariantCulture);
                double longitude = double.Parse((string)data[0].lon, System.Globalization.CultureInfo.InvariantCulture);
                return (Latitude: latitude, Longitude: longitude);
            }
            throw new Exception("Could not find coordinates for the specified location in Uganda.");
        }

        public async Task<(double Latitude, double Longitude)?> GetCoordinatesByLocationAsync(string location)
        {
            try
            {
                var searchQuery = Uri.EscapeDataString(location + ", Uganda");
                var url = $"?format=json&q={searchQuery}&countrycodes=ug&limit=1";
                _logger.LogInformation("Geocoding location: {Location}, URL: {URL}", location, url);
                
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();

                var data = JsonConvert.DeserializeObject<dynamic>(json);
                if (data != null && data.Count > 0)
                {
                    double latitude = double.Parse((string)data[0].lat, System.Globalization.CultureInfo.InvariantCulture);
                    double longitude = double.Parse((string)data[0].lon, System.Globalization.CultureInfo.InvariantCulture);
                    _logger.LogInformation("Found coordinates for {Location}: {Lat}, {Lon}", location, latitude, longitude);
                    return (Latitude: latitude, Longitude: longitude);
                }
                _logger.LogWarning("No coordinates found for location: {Location}", location);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting coordinates for location: {Location}", location);
                return null;
            }
        }

        public async Task<string?> GetLocationNameAsync(string location)
        {
            try
            {
                var coords = await GetCoordinatesByLocationAsync(location);
                if (coords == null) return null;
                
                var url = $"reverse?format=json&lat={coords.Value.Latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={coords.Value.Longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonConvert.DeserializeObject<dynamic>(json);
                return data?.display_name ?? $"{location}, Uganda";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting location name for: {Location}", location);
                return $"{location}, Uganda";
            }
        }
    }
}
