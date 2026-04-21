
using DRC.Api.Data;
using DRC.Api.Data.Entities;
using DRC.Api.Interfaces;
using DRC.Api.Services;
using Microsoft.EntityFrameworkCore;
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

            // Configure Database
            // Use PostgreSQL if DATABASE_URL is set (Render), otherwise SQLite
            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
            
            if (!string.IsNullOrEmpty(databaseUrl))
            {
                // Parse Render's PostgreSQL URL format: postgres://user:password@host:port/database
                var uri = new Uri(databaseUrl);
                var userInfo = uri.UserInfo.Split(':');
                var port = uri.Port > 0 ? uri.Port : 5432; // Default PostgreSQL port if not specified
                var connectionString = $"Host={uri.Host};Port={port};Database={uri.AbsolutePath.TrimStart('/')};Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
                Console.WriteLine($"Using PostgreSQL database at: {uri.Host}:{port}");
                
                builder.Services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseNpgsql(connectionString));
            }
            else
            {
                // Local development: use SQLite
                var dataDir = Directory.Exists("/app/data") ? "/app/data" : ".";
                var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                    ?? $"Data Source={dataDir}/drc.db";
                Console.WriteLine($"Using SQLite database: {connectionString}");
                
                builder.Services.AddDbContext<ApplicationDbContext>(options =>
                    options.UseSqlite(connectionString));
            }

            // Auth removed — all endpoints are publicly accessible

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
            var isProduction = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production";
            
            if (!string.IsNullOrEmpty(redisConnectionString) && !isProduction)
            {
                // Only use Redis in local dev (docker-compose) — Render free Redis is unreliable
                builder.AddRedisDistributedCache("redis");
                Console.WriteLine("Using Redis distributed cache");
            }
            else
            {
                // In-memory cache works great for hackathon demos
                builder.Services.AddDistributedMemoryCache();
                Console.WriteLine("Using in-memory cache (no Redis)");
            }

            builder.Services.AddScoped<ICepService, CepService>();
            builder.Services.AddScoped<IChatCacheService, ChatCacheService>();
            builder.Services.AddScoped<IEmergencyAlertService, EmergencyAlertService>();
            builder.Services.AddScoped<IChatService, ChatService>();
            builder.Services.AddScoped<IWhatAppService, WhatsAppCloudService>();
            builder.Services.AddScoped<IEmailService, EmailService>();
            builder.Services.AddScoped<ISmsService, AfricasTalkingSmsService>();
            builder.Services.AddScoped<IFacilityAssignmentService, FacilityAssignmentService>();
            builder.Services.AddScoped<IAgentService, AgentService>();
            // Register Lazy<IWhatAppService> to break circular dependency
            builder.Services.AddScoped(sp => new Lazy<IWhatAppService>(() => sp.GetRequiredService<IWhatAppService>()));
            builder.Services.AddScoped<IAuthService, AuthService>();

            builder.Services.AddControllers()
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                });
            
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Real-time push to admin dashboard + chat UI
            builder.Services.AddSignalR();
            builder.Services.AddSingleton<DRC.Api.Services.ILiveNotifier, DRC.Api.Services.LiveNotifier>();

            // Add CORS for frontend
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.SetIsOriginAllowed(_ => true) // SignalR needs credentials, so can't use AllowAnyOrigin
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials();
                });
            });

            var app = builder.Build();

            // Auto-migrate database. EnsureCreated() builds the full schema via EF Core
            // on both SQLite (local) and PostgreSQL (Neon/Render). The legacy SQLite-only
            // "CREATE TABLE IF NOT EXISTS / ALTER TABLE" patches below are only needed
            // for pre-existing local SQLite databases created before those columns existed.
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Database.EnsureCreated();

                var isSqlite = db.Database.IsSqlite();

                if (isSqlite)
                {
                    // Legacy patches — only run against SQLite. On Postgres these are
                    // unnecessary (EnsureCreated already created everything) and the
                    // syntax ("AUTOINCREMENT", bare ALTER) isn't valid PG anyway.
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
                        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ChatMessages_SessionId ON ChatMessages(SessionId)");
                        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ChatMessages_UserId ON ChatMessages(UserId)");
                        db.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ChatMessages_CreatedAt ON ChatMessages(CreatedAt)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Note: ChatMessages table setup: {ex.Message}");
                    }

                    try { db.Database.ExecuteSqlRaw("ALTER TABLE EmergencyContacts ADD COLUMN WhatsAppNumber TEXT"); } catch { }
                    try { db.Database.ExecuteSqlRaw("ALTER TABLE EmergencyContacts ADD COLUMN Email TEXT"); } catch { }
                    try { db.Database.ExecuteSqlRaw("ALTER TABLE EmergencyRequests ADD COLUMN AssignedFacilityId INTEGER"); } catch { }
                }

                // Seed default admin user (only creates if doesn't exist)
                try
                {
                    var adminEmail = "admin@drc.ug";
                    var adminPassword = "Admin123!";
                    var existingAdmin = db.Users.FirstOrDefault(u => u.Email == adminEmail);
                    
                    if (existingAdmin == null)
                    {
                        var adminUser = new DRC.Api.Data.Entities.User
                        {
                            FullName = "DRC Administrator",
                            Email = adminEmail,
                            Phone = "+256700000000",
                            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                            Role = DRC.Api.Data.Entities.UserRole.Admin,
                            IsActive = true,
                            CreatedAt = DateTime.UtcNow
                        };
                        db.Users.Add(adminUser);
                        db.SaveChanges();
                        Console.WriteLine("[OK] Default admin user created: admin@drc.ug / Admin123!");
                    }
                    else
                    {
                        // Reset admin password, role and active status
                        existingAdmin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword);
                        existingAdmin.IsActive = true;
                        existingAdmin.Role = DRC.Api.Data.Entities.UserRole.Admin;
                        db.SaveChanges();
                        Console.WriteLine("[OK] Admin user password reset: admin@drc.ug / Admin123!");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Note: Admin user seeding: {ex.Message}");
                }

                // Seed judge user accounts for hackathon demo
                try
                {
                    var judgeAccounts = new[]
                    {
                        ("drc@africastalking.ug", "AT Judge", "+256701000001"),
                    };
                    var judgePassword = "Judge2026!";

                    foreach (var (email, name, phone) in judgeAccounts)
                    {
                        var existingJudge = db.Users.FirstOrDefault(u => u.Email == email);
                        if (existingJudge == null)
                        {
                            var judgeUser = new DRC.Api.Data.Entities.User
                            {
                                FullName = name,
                                Email = email,
                                Phone = phone,
                                PasswordHash = BCrypt.Net.BCrypt.HashPassword(judgePassword),
                                Role = DRC.Api.Data.Entities.UserRole.User,
                                IsActive = true,
                                CreatedAt = DateTime.UtcNow
                            };
                            db.Users.Add(judgeUser);
                            Console.WriteLine($"[OK] Judge user created: {email} / {judgePassword}");
                        }
                        else
                        {
                            // Always reset judge password on startup to ensure correct credentials
                            existingJudge.PasswordHash = BCrypt.Net.BCrypt.HashPassword(judgePassword);
                            existingJudge.FullName = name;
                            existingJudge.IsActive = true;
                            Console.WriteLine($"[OK] Judge user password reset: {email} / {judgePassword}");
                        }
                    }
                    db.SaveChanges();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Note: Judge user seeding: {ex.Message}");
                }

                // Seed default facilities for Uganda
                try
                {
                    if (!db.Facilities.Any())
                    {
                        var facilities = new List<Facility>
                        {
                            // Hospitals
                            new Facility
                            {
                                Name = "Mulago National Referral Hospital",
                                Type = FacilityType.Hospital,
                                Address = "Mulago Hill, Kampala",
                                Latitude = 0.3404,
                                Longitude = 32.5759,
                                Phone = "+256414541884",
                                Description = "Uganda's largest national referral hospital",
                                Capacity = 1500,
                                CurrentOccupancy = 0,
                                IsOperational = true,
                                Is24Hours = true,
                                ServicesOffered = "Emergency, Surgery, ICU, Maternity, Pediatrics"
                            },
                            new Facility
                            {
                                Name = "Mbale Regional Referral Hospital",
                                Type = FacilityType.Hospital,
                                Address = "Pallisa Road, Mbale",
                                Latitude = 1.0644,
                                Longitude = 34.1747,
                                Phone = "+256454435678",
                                Description = "Eastern Uganda regional referral hospital",
                                Capacity = 400,
                                CurrentOccupancy = 0,
                                IsOperational = true,
                                Is24Hours = true,
                                ServicesOffered = "Emergency, Surgery, Maternity, Pediatrics"
                            },
                            new Facility
                            {
                                Name = "Mbarara Regional Referral Hospital",
                                Type = FacilityType.Hospital,
                                Address = "Hospital Road, Mbarara",
                                Latitude = -0.6063,
                                Longitude = 30.6545,
                                Phone = "+256485421566",
                                Description = "Western Uganda regional referral hospital",
                                Capacity = 350,
                                CurrentOccupancy = 0,
                                IsOperational = true,
                                Is24Hours = true,
                                ServicesOffered = "Emergency, Surgery, Maternity, Trauma"
                            },
                            new Facility
                            {
                                Name = "Gulu Regional Referral Hospital",
                                Type = FacilityType.Hospital,
                                Address = "Gulu Town, Gulu",
                                Latitude = 2.7746,
                                Longitude = 32.2990,
                                Phone = "+256471432100",
                                Description = "Northern Uganda regional referral hospital",
                                Capacity = 300,
                                CurrentOccupancy = 0,
                                IsOperational = true,
                                Is24Hours = true,
                                ServicesOffered = "Emergency, Surgery, Pediatrics"
                            },
                            
                            // Shelters
                            new Facility
                            {
                                Name = "Nakivale Refugee Settlement",
                                Type = FacilityType.Shelter,
                                Address = "Isingiro District",
                                Latitude = -0.7833,
                                Longitude = 30.9167,
                                Phone = "+256800100066",
                                Description = "UNHCR managed refugee settlement",
                                Capacity = 5000,
                                CurrentOccupancy = 0,
                                IsOperational = true,
                                Is24Hours = true,
                                ServicesOffered = "Shelter, Food, Medical, Education"
                            },
                            new Facility
                            {
                                Name = "Kampala Emergency Shelter - Namuwongo",
                                Type = FacilityType.Shelter,
                                Address = "Namuwongo, Kampala",
                                Latitude = 0.2986,
                                Longitude = 32.6027,
                                Phone = "+256414123456",
                                Description = "Urban emergency shelter for displaced persons",
                                Capacity = 200,
                                CurrentOccupancy = 0,
                                IsOperational = true,
                                Is24Hours = true,
                                ServicesOffered = "Shelter, Food, Basic Medical"
                            },
                            new Facility
                            {
                                Name = "Bududa Disaster Relief Camp",
                                Type = FacilityType.Shelter,
                                Address = "Bududa Town",
                                Latitude = 1.0067,
                                Longitude = 34.3333,
                                Phone = "+256800100250",
                                Description = "Landslide disaster relief shelter",
                                Capacity = 500,
                                CurrentOccupancy = 0,
                                IsOperational = true,
                                Is24Hours = true,
                                ServicesOffered = "Emergency Shelter, Food, Trauma Support"
                            },
                            
                            // Police Stations
                            new Facility
                            {
                                Name = "Central Police Station Kampala",
                                Type = FacilityType.PoliceStation,
                                Address = "Kampala Road, Kampala",
                                Latitude = 0.3163,
                                Longitude = 32.5822,
                                Phone = "999",
                                Description = "Main police station for Kampala",
                                IsOperational = true,
                                Is24Hours = true,
                                ServicesOffered = "Emergency Response, Reporting, Investigations"
                            },
                            new Facility
                            {
                                Name = "Jinja Road Police Station",
                                Type = FacilityType.PoliceStation,
                                Address = "Jinja Road, Kampala",
                                Latitude = 0.3177,
                                Longitude = 32.6023,
                                Phone = "999",
                                Description = "Police station serving eastern Kampala",
                                IsOperational = true,
                                Is24Hours = true,
                                ServicesOffered = "Emergency Response, Traffic, Patrols"
                            },
                            
                            // Fire Stations
                            new Facility
                            {
                                Name = "Kampala Fire & Rescue Brigade HQ",
                                Type = FacilityType.FireStation,
                                Address = "Bukoto, Kampala",
                                Latitude = 0.3497,
                                Longitude = 32.5943,
                                Phone = "+256414340440",
                                Description = "Main fire brigade headquarters",
                                IsOperational = true,
                                Is24Hours = true,
                                ServicesOffered = "Fire Fighting, Rescue Operations, Emergency Response"
                            },
                            
                            // Evacuation Points
                            new Facility
                            {
                                Name = "Kololo Airstrip Evacuation Point",
                                Type = FacilityType.EvacuationPoint,
                                Address = "Kololo, Kampala",
                                Latitude = 0.3280,
                                Longitude = 32.5876,
                                Phone = "+256800100066",
                                Description = "Emergency evacuation and helicopter landing site",
                                Capacity = 1000,
                                IsOperational = true,
                                ServicesOffered = "Air Evacuation, Assembly Point"
                            },
                            new Facility
                            {
                                Name = "Nakasero Market Assembly Point",
                                Type = FacilityType.EvacuationPoint,
                                Address = "Nakasero, Kampala",
                                Latitude = 0.3128,
                                Longitude = 32.5780,
                                Phone = "+256800100066",
                                Description = "Urban emergency assembly point",
                                Capacity = 500,
                                IsOperational = true,
                                ServicesOffered = "Assembly Point, First Aid"
                            },
                            
                            // Food Distribution
                            new Facility
                            {
                                Name = "WFP Kampala Distribution Center",
                                Type = FacilityType.FoodDistribution,
                                Address = "Industrial Area, Kampala",
                                Latitude = 0.3050,
                                Longitude = 32.6150,
                                Phone = "+256414230094",
                                Description = "World Food Programme distribution center",
                                Capacity = 10000,
                                IsOperational = true,
                                OperatingHours = "8:00 AM - 5:00 PM",
                                ServicesOffered = "Food Distribution, Emergency Supplies"
                            },
                            
                            // Water Points
                            new Facility
                            {
                                Name = "NWSC Emergency Water Point - Kampala",
                                Type = FacilityType.WaterPoint,
                                Address = "Kisenyi, Kampala",
                                Latitude = 0.3100,
                                Longitude = 32.5700,
                                Phone = "+256800100977",
                                Description = "Emergency water distribution point",
                                IsOperational = true,
                                OperatingHours = "6:00 AM - 8:00 PM",
                                ServicesOffered = "Clean Water, Water Purification"
                            }
                        };
                        
                        db.Facilities.AddRange(facilities);
                        db.SaveChanges();
                        Console.WriteLine($"[OK] Seeded {facilities.Count} facilities for Uganda");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Note: Facility seeding: {ex.Message}");
                }
            }

            app.MapDefaultEndpoints();

            // Enable Swagger in all environments for API documentation
            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseHttpsRedirection();

            app.UseCors();

            // Auth middleware removed — endpoints are open

            // Root endpoint
            app.MapGet("/", () => Results.Ok(new { 
                name = "Disaster Response Coordinator API",
                status = "running",
                docs = "/swagger"
            }));

            // Deep health check — verifies DB connectivity (used by uptime monitors)
            app.MapGet("/api/health", async (ApplicationDbContext db) =>
            {
                try
                {
                    var canConnect = await db.Database.CanConnectAsync();
                    var provider = db.Database.ProviderName ?? "unknown";
                    return Results.Ok(new
                    {
                        status = canConnect ? "healthy" : "degraded",
                        db = canConnect ? "up" : "down",
                        provider,
                        time = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    return Results.Json(new { status = "unhealthy", error = ex.Message }, statusCode: 503);
                }
            });

            app.MapControllers();
            app.MapHub<DRC.Api.Hubs.LiveHub>("/hubs/live");

            app.Run();
        }
    }
}
