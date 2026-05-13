using System.Text.Json;
using DeliverySimulator.Database;
using DeliverySimulator.Database.Models;
using Microsoft.EntityFrameworkCore;

namespace DeliverySimulator.Database;

public static class Seeder
{
    /// <summary>
    /// Teljes adatbázis-frissítés:
    /// – eltávolítja a Data/ mappából törölt városokat (és azok összes adatát),
    /// – hozzáadja az új, még nem szereplő városokat.
    /// SimulationRuns / DeliveryLogs megmaradnak.
    /// </summary>
    public static async Task RefreshAsync(AppDbContext db)
    {
        var basePath = Path.Combine(AppContext.BaseDirectory, "Data");
        if (!Directory.Exists(basePath))
        {
            Console.WriteLine("  [Figyelem] Data/ mappa nem található.");
            return;
        }

        // 1. Gyűjtsük össze a Data/-ban lévő városneveket
        var folderCityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.GetDirectories(basePath))
        {
            Console.WriteLine(dir);
            var cityFile = Path.Combine(dir, "city.json");
            if (!File.Exists(cityFile)) continue;

            try
            {
                var doc = JsonDocument.Parse(await File.ReadAllTextAsync(cityFile));
                var name = doc.RootElement.GetProperty("cityName").GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    folderCityNames.Add(name);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [Hiba] {Path.GetFileName(dir)}/city.json olvasása sikertelen: {ex.Message}");
            }
        }

        // 2. Töröljük az adatbázisból azokat a városokat, amelyek mappája már nem létezik
        var dbCities = await db.Cities.ToListAsync();
        int removedCount = 0;

        foreach (var city in dbCities)
        {
            if (folderCityNames.Contains(city.Name)) continue;

            Console.WriteLine($"  [Törlés] {city.Name} eltávolítása...");

            // Manuális kaszkád törlés (nincs FK constraint a sémában)
            var nodes    = await db.Nodes.Where(n => n.CityId == city.Id).ToListAsync();
            var edges    = await db.Edges.Where(e => e.CityId == city.Id).ToListAsync();
            var couriers = await db.Couriers.Where(c => c.CityId == city.Id).ToListAsync();
            var orders   = await db.Orders.Where(o => o.CityId == city.Id).ToListAsync();

            db.Orders.RemoveRange(orders);
            db.Couriers.RemoveRange(couriers);
            db.Edges.RemoveRange(edges);
            db.Nodes.RemoveRange(nodes);
            db.Cities.Remove(city);

            removedCount++;
        }

        if (removedCount > 0)
            await db.SaveChangesAsync();

        // 3. Adjuk hozzá az új városokat (a SeedIfEmptyAsync kihagyja a már meglévőket)
        await SeedIfEmptyAsync(db);

        Console.WriteLine($"  Kész. Törölve: {removedCount} város, hozzáadva: lásd fent.");
    }

    /// <summary>
    /// Csak az adatbázisban még nem szereplő városokat seedi be.
    /// </summary>
    public static async Task SeedIfEmptyAsync(AppDbContext db)
    {
        var basePath = Path.Combine(AppContext.BaseDirectory, "Data");
        if (!Directory.Exists(basePath)) return;

        var existingCityNames = await db.Cities.Select(c => c.Name).ToHashSetAsync();

        foreach (var dir in Directory.GetDirectories(basePath))
        {
            Console.WriteLine(dir);
            var cityFile = Path.Combine(dir, "city.json");
            if (!File.Exists(cityFile)) continue;
            var courierFile = Path.Combine(dir, "couriers.json");
            var orderFile   = Path.Combine(dir, "orders.json");

            var cityJson = JsonDocument.Parse(await File.ReadAllTextAsync(cityFile)).RootElement;
            var cityName = cityJson.GetProperty("cityName").GetString() ?? Path.GetFileName(dir);

            if (existingCityNames.Contains(cityName)) continue;

            Console.WriteLine($"  [Hozzáadás] {cityName}...");

            var city = new City { Name = cityName };
            db.Cities.Add(city);
            await db.SaveChangesAsync();

            int nodeOffset = (city.Id - 1) * 1000;

            var nodes = cityJson.GetProperty("nodes").EnumerateArray().ToList();
            foreach (var n in nodes)
            {
                db.Nodes.Add(new Node
                {
                    Id     = n.GetProperty("id").GetInt32() + nodeOffset,
                    CityId = city.Id,
                    Name   = n.GetProperty("name").GetString() ?? "",
                    Type   = n.GetProperty("type").GetString() ?? "",
                    ZoneId = n.TryGetProperty("zoneId", out var z) && z.ValueKind != JsonValueKind.Null
                                 ? z.GetInt32() : null
                });
            }

            foreach (var e in cityJson.GetProperty("edges").EnumerateArray())
            {
                int from = e.GetProperty("from").GetInt32() + nodeOffset;
                int to   = e.GetProperty("to").GetInt32()   + nodeOffset;
                int min  = e.GetProperty("idealTimeMinutes").GetInt32();
                db.Edges.Add(new Edge { CityId = city.Id, From = from, To = to,   IdealMinutes = min });
                db.Edges.Add(new Edge { CityId = city.Id, From = to,   To = from, IdealMinutes = min });
            }

            if (File.Exists(courierFile))
            {
                var couriers = JsonDocument.Parse(await File.ReadAllTextAsync(courierFile))
                                           .RootElement.EnumerateArray();
                foreach (var c in couriers)
                {
                    db.Couriers.Add(new Courier
                    {
                        CityId        = city.Id,
                        Name          = c.GetProperty("name").GetString() ?? "",
                        CurrentNodeId = c.GetProperty("currentNodeId").GetInt32() + nodeOffset,
                        MaxCapacity   = c.GetProperty("maxCapacity").GetInt32(),
                        ZoneIds       = c.GetProperty("zoneIds").EnumerateArray()
                                         .Select(z => z.GetInt32()).ToList()
                    });
                }
            }

            if (File.Exists(orderFile))
            {
                var orders = JsonDocument.Parse(await File.ReadAllTextAsync(orderFile))
                                         .RootElement.EnumerateArray();
                foreach (var o in orders)
                {
                    db.Orders.Add(new Order
                    {
                        CityId        = city.Id,
                        Number        = o.GetProperty("number").GetString() ?? "",
                        Customer      = o.GetProperty("customer").GetString() ?? "",
                        Address       = o.GetProperty("address").GetString() ?? "",
                        AddressNodeId = o.GetProperty("addressNodeId").GetInt32() + nodeOffset,
                        ZoneId        = o.GetProperty("zoneId").GetInt32()
                    });
                }
            }

            await db.SaveChangesAsync();
            existingCityNames.Add(cityName); // lokálisan is frissítjük, hogy a következő iteráció ne duplikáljon
        }
    }
}
