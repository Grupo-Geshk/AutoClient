using AutoClient.Models;
using Microsoft.EntityFrameworkCore;

namespace AutoClient.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Workshop> Workshops { get; set; }
    public DbSet<Client> Clients { get; set; }
    public DbSet<Vehicle> Vehicles { get; set; }
    public DbSet<Service> Services { get; set; }
    public DbSet<Worker> Workers { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<InvoiceItem> InvoiceItems { get; set; }
    public DbSet<ServiceType> ServiceTypes { get; set; }
    public DbSet<LoginOtp> LoginOtps{ get; set; }
    public DbSet<TrustedDevice> TrustedDevices{ get; set; }
    public DbSet<EmailLog> EmailLogs { get; set; }
    public DbSet<WorkshopNotificationSettings> WorkshopNotificationSettings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Workshop>()
            .HasMany(w => w.Clients)
            .WithOne(c => c.Workshop)
            .HasForeignKey(c => c.WorkshopId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Client>()
            .HasMany(c => c.Vehicles)
            .WithOne(v => v.Client)
            .HasForeignKey(v => v.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Vehicle>()
            .HasMany(v => v.Services)
            .WithOne(s => s.Vehicle)
            .HasForeignKey(s => s.VehicleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Workshop>()
            .HasMany(w => w.Workers)
            .WithOne(worker => worker.Workshop)
            .HasForeignKey(worker => worker.WorkshopId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Worker>()
            .HasMany<Service>()
            .WithOne(s => s.Worker)
            .HasForeignKey(s => s.WorkerId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Workshop>()
            .HasMany<ServiceType>()
            .WithOne(st => st.Workshop)
            .HasForeignKey(st => st.WorkshopId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
