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
    }
}
