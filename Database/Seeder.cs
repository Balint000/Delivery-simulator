using System.Text.Json;
using DeliverySimulator.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace DeliverySimulator.Database;

public static class Seeder
{
    public static async Task SeedIfEmptyAsync(AppDbContext db)
    {
        if (await db.Cities.AnyAsync()) return;

        string basePath = AppContext.BaseDirectory;

        int cityId = await SeedCityAsync(db, basePath);
        await SeedCouriersAsync(db, basePath, cityId);
        await SeedOrdersAsync(db, basePath, cityId);
        await db.SaveChangesAsync();
    }

    // Task<int> → visszaadja a létrejött város Id-jét
    private static async Task<int> SeedCityAsync(AppDbContext db, string basePath)
    {
        var path = Path.Combine(basePath, "Data", "city.json");
        if (!File.Exists(path)) return 0;

        var json = JsonDocument.Parse(await File.ReadAllTextAsync(path)).RootElement;

        var city = new City { Name = json.GetProperty("cityName").GetString()! };
        db.Cities.Add(city);
        await db.SaveChangesAsync(); // kell az auto-generált Id-hez

        foreach (var n in json.GetProperty("nodes").EnumerateArray())
            db.Nodes.Add(new Node
            {
                Id = n.GetProperty("id").GetInt32(),
                CityId = city.Id,
                Name = n.GetProperty("name").GetString()!,
                Type = n.GetProperty("type").GetString()!,
                ZoneId = n.TryGetProperty("zoneId", out var z) && z.ValueKind != JsonValueKind.Null
                             ? z.GetInt32() : null
            });

        foreach (var e in json.GetProperty("edges").EnumerateArray())
        {
            int from = e.GetProperty("from").GetInt32();
            int to = e.GetProperty("to").GetInt32();
            int min = e.GetProperty("idealTimeMinutes").GetInt32();
            // Irányítatlan → mindkét irány mentve
            db.Edges.Add(new Edge { CityId = city.Id, From = from, To = to, IdealMinutes = min });
            db.Edges.Add(new Edge { CityId = city.Id, From = to, To = from, IdealMinutes = min });
        }

        await db.SaveChangesAsync();
        return city.Id;
    }

    private static async Task SeedCouriersAsync(AppDbContext db, string basePath, int cityId)
    {
        var path = Path.Combine(basePath, "Data", "couriers.json");
        if (!File.Exists(path)) return;

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = JsonSerializer.Deserialize<List<Courier>>(
                       await File.ReadAllTextAsync(path), opts) ?? [];

        foreach (var c in list) c.CityId = cityId;
        db.Couriers.AddRange(list);
    }

    private static async Task SeedOrdersAsync(AppDbContext db, string basePath, int cityId)
    {
        var path = Path.Combine(basePath, "Data", "orders.json");
        if (!File.Exists(path)) return;

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = JsonSerializer.Deserialize<List<Order>>(
                       await File.ReadAllTextAsync(path), opts) ?? [];

        foreach (var o in list) o.CityId = cityId;
        db.Orders.AddRange(list);
    }
}
