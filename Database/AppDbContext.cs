using Microsoft.EntityFrameworkCore;
using DeliverySimulator.Database.Entities;

namespace DeliverySimulator.Database;

public class AppDbContext : DbContext
{
    public DbSet<CityEntity> Cities => Set<CityEntity>();
    public DbSet<NodeEntity> Nodes => Set<NodeEntity>();
    public DbSet<EdgeEntity> Edges => Set<EdgeEntity>();
    public DbSet<CourierEntity> Couriers => Set<CourierEntity>();
    public DbSet<OrderEntity> Orders => Set<OrderEntity>();
    public DbSet<SimulationRunEntity> SimulationRuns => Set<SimulationRunEntity>();
    public DbSet<DeliveryLogEntity> DeliveryLogs => Set<DeliveryLogEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = Path.Combine(AppContext.BaseDirectory, "delivery.db");
        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // EdgeEntity: nincs navigációs property a Node-okhoz (csak FK),
        // hogy elkerüljük a körkörös cascade delete-et
        modelBuilder.Entity<EdgeEntity>()
            .HasOne<NodeEntity>()
            .WithMany()
            .HasForeignKey(e => e.FromNodeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<EdgeEntity>()
            .HasOne<NodeEntity>()
            .WithMany()
            .HasForeignKey(e => e.ToNodeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
