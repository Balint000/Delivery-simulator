using Microsoft.EntityFrameworkCore;
using DeliverySimulator.Database.Models;

namespace DeliverySimulator.Database;

// Város — csak a DB-nek kell, nincs Models-ben
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

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "delivery.db");
        options.UseSqlite($"Data Source={path}");
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        // ZoneIds lista → vesszővel elválasztott szöveg
        model.Entity<Courier>()
            .Property(c => c.ZoneIds)
            .HasConversion(
                v => string.Join(',', v),
                v => v == "" ? new List<int>() :
                     v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                      .Select(int.Parse).ToList()
            );
    }
}
