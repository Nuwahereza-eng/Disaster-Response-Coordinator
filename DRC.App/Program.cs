using DRC.App.Components;
using DRC.App.Services;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;

namespace DRC.App
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.AddServiceDefaults();
            
            // Only load static web assets manifest in development (doesn't exist in published builds)
            if (builder.Environment.IsDevelopment())
            {
                StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
            }

            // Get API URL from environment - ApiUrl takes priority (set manually on Render)
            var apiUrl = Environment.GetEnvironmentVariable("ApiUrl")
                ?? builder.Configuration["ApiUrl"]
                ?? Environment.GetEnvironmentVariable("services__api__http__0")
                ?? builder.Configuration["services:api:http:0"] 
                ?? (builder.Environment.IsProduction() 
                    ? "https://drc-api-sttm.onrender.com" 
                    : "http://localhost:5099");
            
            // Ensure URL has a protocol prefix (Render's fromService may return just hostname)
            if (!apiUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !apiUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                apiUrl = "https://" + apiUrl;
            }
            
            Console.WriteLine($"API URL configured as: {apiUrl}");

            builder.Services.AddHttpClient<AgentClientService>(client =>
            {
                // Use localhost for local development, "http://api" for Docker/Aspire
                client.BaseAddress = new Uri(apiUrl);
                client.Timeout = TimeSpan.FromMinutes(5); // Increased timeout for AI responses
            });

            // Admin service for admin panel
            builder.Services.AddHttpClient<AdminClientService>(client =>
            {
                client.BaseAddress = new Uri(apiUrl);
                client.Timeout = TimeSpan.FromMinutes(2); // Increased for slow Render free tier
            });

            // User service for profile and history - needs HttpClient and IJSRuntime
            builder.Services.AddHttpClient("UserApi", client =>
            {
                client.BaseAddress = new Uri(apiUrl);
                client.Timeout = TimeSpan.FromMinutes(2); // Increased for slow Render free tier
            });
            builder.Services.AddScoped<UserClientService>(sp =>
            {
                var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient("UserApi");
                var jsRuntime = sp.GetRequiredService<Microsoft.JSInterop.IJSRuntime>();
                return new UserClientService(httpClient, jsRuntime);
            });
            
            // Configure Blazor Server circuit options for longer AI response times
            builder.Services.AddServerSideBlazor()
                .AddCircuitOptions(options =>
                {
                    options.DetailedErrors = true;
                    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(3);
                })
                .AddHubOptions(options =>
                {
                    options.ClientTimeoutInterval = TimeSpan.FromMinutes(5);
                    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                    options.HandshakeTimeout = TimeSpan.FromMinutes(1);
                    options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB
                });
            
            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            var app = builder.Build();

            app.MapDefaultEndpoints();

            // Health check endpoint
            app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // Note: No HSTS or HTTPS redirect — Render handles SSL termination at the load balancer
            }

            app.UseStaticFiles();
            app.UseAntiforgery();

            app.MapRazorComponents<Components.App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
