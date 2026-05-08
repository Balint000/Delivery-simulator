using System.Text.Json;
using DeliverySimulator.Database.Entities;
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

        var city = new CityEntity { Name = json.GetProperty("cityName").GetString() ?? "Unknown" };
        db.Cities.Add(city);
        await db.SaveChangesAsync();

        // jsonId (0,1,2...) → DB-beli NodeEntity.Id leképezés
        var nodeIdMap = new Dictionary<int, int>();

        foreach (var n in json.GetProperty("nodes").EnumerateArray())
        {
            var node = new NodeEntity
            {
                CityId = city.Id,
                Name = n.GetProperty("name").GetString() ?? "",
                Zone = n.TryGetProperty("type", out var t) ? t.GetString() ?? "" : ""
            };
            db.Nodes.Add(node);
            await db.SaveChangesAsync(); // ← kell, hogy megkapjuk az auto-increment Id-t

            int jsonId = n.GetProperty("id").GetInt32();
            nodeIdMap[jsonId] = node.Id; // ← eltároljuk: json 0 → db 1, json 1 → db 2, stb.
        }

        foreach (var e in json.GetProperty("edges").EnumerateArray())
        {
            db.Edges.Add(new EdgeEntity
            {
                CityId = city.Id,
                FromNodeId = nodeIdMap[e.GetProperty("from").GetInt32()], // ← DB Id!
                ToNodeId = nodeIdMap[e.GetProperty("to").GetInt32()],   // ← DB Id!
                Distance = e.GetProperty("idealTimeMinutes").GetDouble()
            });
        }
        // Az éleket nem kell külön SaveChangesAsync-kal menteni,
        // a SeedIfEmptyAsync végén lévő SaveChangesAsync elvégzi.
    }

    private static async Task SeedCouriersAsync(AppDbContext db)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "couriers.json");
        if (!File.Exists(path)) return;
        var list = JsonSerializer.Deserialize<List<JsonElement>>(await File.ReadAllTextAsync(path)) ?? [];

        foreach (var c in list)
        {
            db.Couriers.Add(new CourierEntity
            {
                Name = c.GetProperty("name").GetString() ?? "",
                Capacity = c.GetProperty("maxCapacity").GetInt32(),
                Speed = 1.0 // Default speed as it's required in CourierEntity.cs
            });
        }
    }

    private static async Task SeedOrdersAsync(AppDbContext db)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "orders.json");
        if (!File.Exists(path)) return;
        var list = JsonSerializer.Deserialize<List<JsonElement>>(await File.ReadAllTextAsync(path)) ?? [];

        foreach (var o in list)
        {
            db.Orders.Add(new OrderEntity
            {
                Customer = o.GetProperty("customer").GetString() ?? "",
                DestinationNodeId = o.GetProperty("addressNodeId").GetInt32(),
                Zone = o.GetProperty("zoneId").ToString(), // Converting to string to match OrderEntity.cs
                Priority = 1 // Default value
            });
        }
    }
}
