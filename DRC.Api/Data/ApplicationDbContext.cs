using Microsoft.EntityFrameworkCore;
using DRC.Api.Data.Entities;

namespace DRC.Api.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<EmergencyRequest> EmergencyRequests { get; set; }
        public DbSet<ShelterRegistration> ShelterRegistrations { get; set; }
        public DbSet<EvacuationRequest> EvacuationRequests { get; set; }
        public DbSet<EmergencyContact> EmergencyContacts { get; set; }
        public DbSet<Facility> Facilities { get; set; }
        public DbSet<AlertNotification> AlertNotifications { get; set; }
        public DbSet<AgentSession> AgentSessions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(e => e.Email).IsUnique();
                entity.HasIndex(e => e.Phone).IsUnique();
            });

            // EmergencyRequest configuration
            modelBuilder.Entity<EmergencyRequest>(entity =>
            {
                entity.HasOne(e => e.User)
                    .WithMany(u => u.EmergencyRequests)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // ShelterRegistration configuration
            modelBuilder.Entity<ShelterRegistration>(entity =>
            {
                entity.HasOne(e => e.User)
                    .WithMany(u => u.ShelterRegistrations)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // EvacuationRequest configuration
            modelBuilder.Entity<EvacuationRequest>(entity =>
            {
                entity.HasOne(e => e.User)
                    .WithMany(u => u.EvacuationRequests)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // EmergencyContact configuration
            modelBuilder.Entity<EmergencyContact>(entity =>
            {
                entity.HasOne(e => e.User)
                    .WithMany(u => u.EmergencyContacts)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // AlertNotification configuration
            modelBuilder.Entity<AlertNotification>(entity =>
            {
                entity.HasOne(e => e.User)
                    .WithMany(u => u.AlertNotifications)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // Seed admin user
            modelBuilder.Entity<User>().HasData(new User
            {
                Id = 1,
                FullName = "System Administrator",
                Email = "admin@drc.ug",
                Phone = "+256700000000",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                Role = UserRole.Admin,
                IsActive = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        }
    }
}
