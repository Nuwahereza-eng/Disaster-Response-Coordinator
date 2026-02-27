
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

            // Configure SQLite Database
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                ?? "Data Source=drc.db";
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

            builder.AddRedisDistributedCache("redis");

            builder.Services.AddScoped<ICepService, CepService>();
            builder.Services.AddScoped<IChatCacheService, ChatCacheService>();
            builder.Services.AddScoped<IEmergencyAlertService, EmergencyAlertService>();
            builder.Services.AddScoped<IChatService, ChatService>();
            builder.Services.AddScoped<IAgentService, AgentService>();
            builder.Services.AddScoped<IWhatAppService, WhatsAppCloudService>();
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
            }

            app.MapDefaultEndpoints();

            
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();

            app.UseCors();

            app.UseAuthentication();
            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
