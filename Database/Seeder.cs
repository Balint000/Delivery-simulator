using System.Text.Json;
using DeliverySimulator.Database;
using DeliverySimulator.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace DeliverySimulator.Database;

public static class Seeder
{
    public static async Task SeedIfEmptyAsync(AppDbContext db)
    {
        if (await db.Cities.AnyAsync()) return;

        var basePath = Path.Combine(AppContext.BaseDirectory, "Data");
        if (!Directory.Exists(basePath)) return;

        // Minden almappát megnézzük
        foreach (var dir in Directory.GetDirectories(basePath))
        {
            var cityFile = Path.Combine(dir, "city.json");
            var courierFile = Path.Combine(dir, "couriers.json");
            var orderFile = Path.Combine(dir, "orders.json");

            if (!File.Exists(cityFile)) continue;

            var cityJson = JsonDocument.Parse(await File.ReadAllTextAsync(cityFile)).RootElement;
            var cityName = cityJson.GetProperty("cityName").GetString() ?? Path.GetFileName(dir);

            var city = new City { Name = cityName };
            db.Cities.Add(city);
            await db.SaveChangesAsync(); // kell az Id-hoz

            // Nodes + Edges
            // Node Id-k: városonként újraindul 0-tól,
            // de DB-ben globálisan egyedinek kell lenniük → offset-et rakunk be
            int nodeOffset = (city.Id - 1) * 1000;

            var nodes = cityJson.GetProperty("nodes").EnumerateArray().ToList();
            foreach (var n in nodes)
            {
                db.Nodes.Add(new Node
                {
                    Id = n.GetProperty("id").GetInt32() + nodeOffset,
                    CityId = city.Id,
                    Name = n.GetProperty("name").GetString() ?? "",
                    Type = n.GetProperty("type").GetString() ?? "",
                    ZoneId = n.TryGetProperty("zoneId", out var z) && z.ValueKind != JsonValueKind.Null
                                 ? z.GetInt32() : null
                });
            }

            foreach (var e in cityJson.GetProperty("edges").EnumerateArray())
            {
                int from = e.GetProperty("from").GetInt32() + nodeOffset;
                int to = e.GetProperty("to").GetInt32() + nodeOffset;
                int min = e.GetProperty("idealTimeMinutes").GetInt32();
                db.Edges.Add(new Edge { CityId = city.Id, From = from, To = to, IdealMinutes = min });
                db.Edges.Add(new Edge { CityId = city.Id, From = to, To = from, IdealMinutes = min });
            }

            // Couriers
            if (File.Exists(courierFile))
            {
                var couriers = JsonDocument.Parse(await File.ReadAllTextAsync(courierFile))
                                           .RootElement.EnumerateArray();
                foreach (var c in couriers)
                {
                    db.Couriers.Add(new Courier
                    {
                        CityId = city.Id,
                        Name = c.GetProperty("name").GetString() ?? "",
                        CurrentNodeId = c.GetProperty("currentNodeId").GetInt32() + nodeOffset,
                        MaxCapacity = c.GetProperty("maxCapacity").GetInt32(),
                        ZoneIds = c.GetProperty("zoneIds").EnumerateArray()
                                         .Select(z => z.GetInt32())
                                         .ToList()
                    });
                }
            }

            // Orders
            if (File.Exists(orderFile))
            {
                var orders = JsonDocument.Parse(await File.ReadAllTextAsync(orderFile))
                                         .RootElement.EnumerateArray();
                foreach (var o in orders)
                {
                    db.Orders.Add(new Order
                    {
                        CityId = city.Id,
                        Number = o.GetProperty("number").GetString() ?? "",
                        Customer = o.GetProperty("customer").GetString() ?? "",
                        Address = o.GetProperty("address").GetString() ?? "",
                        AddressNodeId = o.GetProperty("addressNodeId").GetInt32() + nodeOffset,
                        ZoneId = o.GetProperty("zoneId").GetInt32()
                    });
                }
            }

            await db.SaveChangesAsync();
        }
    }
}
