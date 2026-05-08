using System.Text.Json;
using DeliverySimulator.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace DeliverySimulator.Database;

public static class Seeder
{
    public static async Task SeedIfEmptyAsync(AppDbContext db)
    {
        if (await db.Cities.AnyAsync()) return;
        await SeedCityAsync(db, "Data/city.json");
        await SeedCouriersAsync(db);
        await SeedOrdersAsync(db);
        await db.SaveChangesAsync();
    }

    private static async Task SeedCityAsync(AppDbContext db, string path)
    {
        if (!File.Exists(path)) return;
        var json = JsonDocument.Parse(await File.ReadAllTextAsync(path)).RootElement;

        var city = new City { Name = json.GetProperty("cityName").GetString()! };
        db.Cities.Add(city);
        await db.SaveChangesAsync();

        foreach (var n in json.GetProperty("nodes").EnumerateArray())
            db.Nodes.Add(new Node
            {
                Id = n.GetProperty("id").GetInt32(),
                Name = n.GetProperty("name").GetString()!,
                Type = n.GetProperty("type").GetString()!,
                ZoneId = n.TryGetProperty("zoneId", out var z) && z.ValueKind != JsonValueKind.Null
                             ? z.GetInt32() : null
            });
        await db.SaveChangesAsync();

        foreach (var e in json.GetProperty("edges").EnumerateArray())
        {
            int from = e.GetProperty("from").GetInt32();
            int to = e.GetProperty("to").GetInt32();
            int min = e.GetProperty("idealTimeMinutes").GetInt32();
            // Irányítatlan → mindkét irány mentve
            db.Edges.Add(new Edge { From = from, To = to, IdealMinutes = min });
            db.Edges.Add(new Edge { From = to, To = from, IdealMinutes = min });
        }
    }

    private static async Task SeedCouriersAsync(AppDbContext db)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "couriers.json");
        if (!File.Exists(path)) return;
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = JsonSerializer.Deserialize<List<Courier>>(
                       await File.ReadAllTextAsync(path), opts) ?? [];
        db.Couriers.AddRange(list);
    }

    private static async Task SeedOrdersAsync(AppDbContext db)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "orders.json");
        if (!File.Exists(path)) return;
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = JsonSerializer.Deserialize<List<Order>>(
                       await File.ReadAllTextAsync(path), opts) ?? [];
        db.Orders.AddRange(list);
    }
}
