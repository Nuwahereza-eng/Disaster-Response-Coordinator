
using DRC.Api.Data;
using DRC.Api.Interfaces;
using DRC.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using ViaCep;
using WhatsappBusiness.CloudApi.Configurations;
using WhatsappBusiness.CloudApi.Extensions;

namespace DRC.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.AddServiceDefaults();

            // Configure SQLite Database - use /app/data for persistence in Docker
            var dataDir = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" 
                && Directory.Exists("/app/data") 
                ? "/app/data" 
                : ".";
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                ?? $"Data Source={dataDir}/drc.db";
            Console.WriteLine($"Database path: {connectionString}");
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(connectionString));

            // Configure JWT Authentication
            var jwtKey = builder.Configuration["Jwt:Key"] ?? "DisasterResponseCoordinatorSecretKey2024!@#$%";
            var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "DRC.Api";
            var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "DRC.App";

            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtIssuer,
                        ValidAudience = jwtAudience,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                    };
                });

            builder.Services.AddAuthorization();

            // Gemini API is now configured directly in ChatService using Mscc.GenerativeAI

            builder.Services.AddWhatsAppBusinessCloudApiService(
                new WhatsAppBusinessCloudApiConfig
                {
                    AccessToken = builder.Configuration["Apps:Meta:AccessToken"],
                    AppName = builder.Configuration["Apps:Meta:AppName"],
                    WhatsAppBusinessAccountId = builder.Configuration["Apps:Meta:WhatsAppBusinessAccountId"],
                    WhatsAppBusinessId = builder.Configuration["Apps:Meta:WhatsAppBusinessId"],
                    WhatsAppBusinessPhoneNumberId = builder.Configuration["Apps:Meta:WhatsAppBusinessPhoneNumberId"]
                });

            builder.Services.AddHttpClient<IBenfeitoriaService, BenfeitoriaService>(client =>
            {
                client.BaseAddress = new Uri("https://admin.pqd.benfeitoria.com/");
            });

            builder.Services.AddHttpClient<IViaCepClient, ViaCepClient>(client => 
            { 
                client.BaseAddress = new Uri("https://viacep.com.br/"); 
            });

            builder.Services.AddHttpClient<IS2iDService, S2iDService>(client =>
            {
                client.BaseAddress = new Uri("https://s2id.mi.gov.br");
            });

            builder.Services.AddHttpClient<IGooglePlacesService, GooglePlacesService>(client =>
            {
                // Using OpenStreetMap Overpass API (free, no API key required)
                client.BaseAddress = new Uri("https://overpass-api.de/api/interpreter");
            });

            builder.Services.AddHttpClient<IGeocodingService, GeocodingService>(client =>
            {
                // Using OpenStreetMap Nominatim API (free, no API key required)
                client.BaseAddress = new Uri("https://nominatim.openstreetmap.org/search");
            });

            // Use Redis if connection string is configured, otherwise fall back to in-memory cache
            var redisConnectionString = builder.Configuration.GetConnectionString("redis");
            if (!string.IsNullOrEmpty(redisConnectionString))
            {
                builder.AddRedisDistributedCache("redis");
                Console.WriteLine("Using Redis distributed cache");
            }
            else
            {
                // Fallback to in-memory distributed cache for deployments without Redis
                builder.Services.AddDistributedMemoryCache();
                Console.WriteLine("Using in-memory cache (Redis not configured)");
            }

            builder.Services.AddScoped<ICepService, CepService>();
            builder.Services.AddScoped<IChatCacheService, ChatCacheService>();
            builder.Services.AddScoped<IEmergencyAlertService, EmergencyAlertService>();
            builder.Services.AddScoped<IChatService, ChatService>();
            builder.Services.AddScoped<IWhatAppService, WhatsAppCloudService>();
            builder.Services.AddScoped<IAgentService, AgentService>();
            // Register Lazy<IWhatAppService> to break circular dependency
            builder.Services.AddScoped(sp => new Lazy<IWhatAppService>(() => sp.GetRequiredService<IWhatAppService>()));
            builder.Services.AddScoped<IAuthService, AuthService>();

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                });
            
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Add CORS for frontend
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader();
                });
            });

            var app = builder.Build();

            // Auto-migrate database
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Database.EnsureCreated();
                
                // Ensure ChatMessages table exists (for existing databases)
                try
                {
                    db.Database.ExecuteSqlRaw(@"
                        CREATE TABLE IF NOT EXISTS ChatMessages (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            SessionId TEXT NOT NULL,
                            UserId INTEGER,
                            Role TEXT NOT NULL,
                            Content TEXT NOT NULL,
                            UserPhone TEXT,
                            UserLocation TEXT,
                            Latitude REAL,
                            Longitude REAL,
                            ActionsJson TEXT,
                            CreatedAt TEXT NOT NULL,
                            FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE SET NULL
                        )
                    ");
                    
                    // Create indexes for ChatMessages
                    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ChatMessages_SessionId ON ChatMessages(SessionId)");
                    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ChatMessages_UserId ON ChatMessages(UserId)");
                    db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ChatMessages_CreatedAt ON ChatMessages(CreatedAt)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Note: ChatMessages table setup: {ex.Message}");
                }

                // Seed default admin user (recreated on each startup for ephemeral storage)
                try
                {
                    var adminEmail = "admin@drc.ug";
                    var existingAdmin = db.Users.FirstOrDefault(u => u.Email == adminEmail);
                    if (existingAdmin == null)
                    {
                        var adminUser = new DRC.Api.Data.Entities.User
                        {
                            FullName = "DRC Administrator",
                            Email = adminEmail,
                            Phone = "+256700000000",
                            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
                            Role = DRC.Api.Data.Entities.UserRole.Admin,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        };
                        db.Users.Add(adminUser);
                        db.SaveChanges();
                        Console.WriteLine("✅ Default admin user created: admin@drc.ug / Admin123!");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Note: Admin user seeding: {ex.Message}");
                }
            }

            app.MapDefaultEndpoints();

            // Enable Swagger in all environments for API documentation
            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseHttpsRedirection();

            app.UseCors();

            app.UseAuthentication();
            app.UseAuthorization();

            // Root endpoint
            app.MapGet("/", () => Results.Ok(new { 
                name = "Disaster Response Coordinator API",
                status = "running",
                docs = "/swagger"
            }));

            app.MapControllers();

            app.Run();
        }
    }
}
