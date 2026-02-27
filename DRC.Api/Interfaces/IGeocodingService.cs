namespace DRC.Api.Interfaces
{
    public interface IGeocodingService
    {
        Task<(double Latitude, double Longitude)> GetCoordinatesByPostalCodeAsync(string postalCode);
        Task<(double Latitude, double Longitude)?> GetCoordinatesByLocationAsync(string location);
        Task<string?> GetLocationNameAsync(string location);
    }
}
