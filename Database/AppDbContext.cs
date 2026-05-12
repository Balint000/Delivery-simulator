using Microsoft.EntityFrameworkCore;
using DeliverySimulator.Database.Models;

namespace DeliverySimulator.Database;

public class City
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class AppDbContext : DbContext
{
    public DbSet<City> Cities => Set<City>();
    public DbSet<Node> Nodes => Set<Node>();
    public DbSet<Edge> Edges => Set<Edge>();
    public DbSet<Courier> Couriers => Set<Courier>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<SimulationRun> SimulationRuns => Set<SimulationRun>();
    public DbSet<DeliveryLog> DeliveryLogs => Set<DeliveryLog>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "delivery.db");
        options.UseSqlite($"Data Source={path}");
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Node Id a JSON-ból jön (0-13)
        model.Entity<Node>()
             .Property(n => n.Id)
             .ValueGeneratedNever();

        // ZoneIds lista → vesszővel elválasztott szöveg ("1,2,3")
        model.Entity<Courier>()
             .Property(c => c.ZoneIds)
             .HasConversion(
                 v => string.Join(',', v),
                 v => v == "" ? new List<int>() :
                      v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(int.Parse).ToList());

        // DeliveryLog → SimulationRun kapcsolat
        model.Entity<DeliveryLog>()
             .HasOne<SimulationRun>()
             .WithMany(r => r.Logs)
             .HasForeignKey(d => d.SimRunId);
    }
}
