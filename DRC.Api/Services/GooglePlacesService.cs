using DRC.Api.Interfaces;
using System.Text.Json;
using System.Text;

namespace DRC.Api.Services
{
    public class GooglePlacesService : IGooglePlacesService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public GooglePlacesService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("UgandaDisasterResponse/1.0");
        }

        public async Task<string> GetHospitalsAsync(double latitude, double longitude)
        {
            // Using OpenStreetMap Overpass API for Uganda
            // Search for hospitals, health centers, shelters, police, fire stations within 15km
            var overpassQuery = $@"
                [out:json][timeout:25];
                (
                  node[""amenity""=""hospital""](around:15000,{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                  way[""amenity""=""hospital""](around:15000,{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                  node[""amenity""=""clinic""](around:15000,{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                  node[""healthcare""=""centre""](around:15000,{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                  node[""amenity""=""shelter""](around:15000,{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                  node[""amenity""=""police""](around:15000,{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                  node[""amenity""=""fire_station""](around:15000,{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                  node[""social_facility""](around:15000,{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                  node[""office""=""ngo""](around:15000,{latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)},{longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)});
                );
                out center;
            ";

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("data", overpassQuery)
            });

            var response = await _httpClient.PostAsync("", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

            var filteredResults = new List<Dictionary<string, string>>();
            
            if (jsonResponse.TryGetProperty("elements", out var elements))
            {
                foreach (var element in elements.EnumerateArray())
                {
                    var tags = element.TryGetProperty("tags", out var tagsElement) ? tagsElement : default;
                    
                    var name = "Unknown Facility";
                    var vicinity = "";
                    var facilityType = "Emergency Service";
                    var phone = "";
                    
                    if (tags.ValueKind != JsonValueKind.Undefined)
                    {
                        if (tags.TryGetProperty("name", out var nameElement))
                            name = nameElement.GetString() ?? "Unknown Facility";
                        
                        if (tags.TryGetProperty("amenity", out var amenityElement))
                        {
                            var amenity = amenityElement.GetString();
                            facilityType = amenity switch
                            {
                                "hospital" => "Hospital",
                                "clinic" => "Health Clinic",
                                "shelter" => "Emergency Shelter",
                                "police" => "Police Station",
                                "fire_station" => "Fire Station",
                                _ => "Emergency Service"
                            };
                        }
                        
                        if (tags.TryGetProperty("phone", out var phoneElement))
                            phone = phoneElement.GetString() ?? "";
                        
                        var addressParts = new List<string>();
                        if (tags.TryGetProperty("addr:street", out var street))
                            addressParts.Add(street.GetString());
                        if (tags.TryGetProperty("addr:housenumber", out var number))
                            addressParts.Add(number.GetString());
                        if (tags.TryGetProperty("addr:city", out var city))
                            addressParts.Add(city.GetString());
                        if (tags.TryGetProperty("addr:district", out var district))
                            addressParts.Add(district.GetString());
                        
                        vicinity = string.Join(", ", addressParts.Where(p => !string.IsNullOrEmpty(p)));
                        
                        if (string.IsNullOrEmpty(vicinity) && tags.TryGetProperty("addr:full", out var fullAddr))
                            vicinity = fullAddr.GetString() ?? "";
                    }

                    filteredResults.Add(new Dictionary<string, string>
                    {
                        ["name"] = name,
                        ["type"] = facilityType,
                        ["address"] = vicinity,
                        ["phone"] = phone
                    });
                }
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(filteredResults, options);
        }
    }
}
