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
            StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration); //Add this

            var apiUrl = builder.Configuration["ApiUrl"] ?? "http://localhost:5099";

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
                client.Timeout = TimeSpan.FromSeconds(30);
            });

            // User service for profile and history - needs HttpClient and IJSRuntime
            builder.Services.AddHttpClient("UserApi", client =>
            {
                client.BaseAddress = new Uri(apiUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
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

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseStaticFiles();
            app.UseAntiforgery();

            app.MapRazorComponents<Components.App>()
                .AddInteractiveServerRenderMode();

            app.Run();
        }
    }
}
