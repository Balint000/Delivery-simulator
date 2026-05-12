using DeliverySimulator.Display;
using DeliverySimulator.Graph;
using DeliverySimulator.Database.Models;
using DeliverySimulator.Services;
using DeliverySimulator.Database;
using Microsoft.EntityFrameworkCore;

// 1. database inicializálás
await using var db = new AppDbContext();
await db.Database.MigrateAsync();
await Seeder.SeedIfEmptyAsync(db);

// Főmenü

while (true)
{
    var choice = ConsoleMenu.Show(
        "━━━ Csomag kézbesítés szimuláció ━━━\n\nFőmenü",
        new[]
        {
            "[1] Szimuláció indítása",
            "[2] Rendelés hozzáadása",
            "[3] Helyek listázása",
            "[4] Korábbi futások",
            "[5] Adatbázis frissítése",
            "[6] Kilépés"
        });

    if (choice == -1 || choice == 5)
        break;

    switch (choice)
    {
        case 0: await RunSimulationAsync(db); break;
        case 1: await AddOrderAsync(db); break;
        case 2: await ListPlacesAsync(db); break;
        case 3: await ShowPastRunsAsync(db); break;
        case 4: await Seeder.SeedIfEmptyAsync(db); Console.WriteLine("Frissítés kész."); break;
    }

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("  Nyomj meg egy billentyűt a főmenühöz...");
    Console.ResetColor();
    Console.ReadKey(intercept: true);
}

// Szimuláció futtatása

static async Task RunSimulationAsync(AppDbContext db)
{
    var city = await SelectCityAsync(db, "Szimuláció indítása (válassz várost)");
    if (city == null) return;

    PrintSetupScreen();

    var nodes = await db.Nodes.Where(n => n.CityId == city.Id).ToListAsync();
    var edges = await db.Edges.Where(e => e.CityId == city.Id).ToListAsync();
    var couriers = await db.Couriers.Where(c => c.CityId == city.Id).ToListAsync();
    var orders = await db.Orders.Where(o => o.CityId == city.Id).ToListAsync();

    var graph = new CityGraph(nodes, edges);

    PrintStep("Város", city.Name);
    PrintStep("Városgráf", $"{nodes.Count} csúcs, {edges.Count / 2} él");
    PrintStep("Futárok", $"{couriers.Count} futár betöltve");
    PrintStep("Rendelések", $"{orders.Count} rendelés betöltve");

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("  Nyomj meg egy billentyűt a szimuláció indításához...");
    Console.ResetColor();
    Console.ReadKey(intercept: true);

    var liveConsole = new LiveConsole();
    var greedy = new GreedyAssignmentService(graph);
    var nn = new NearestNeighborService(graph);
    var simulation = new DeliverySimulationService(graph, liveConsole);
    var orchestrator = new SimulationOrchestrator(graph, greedy, nn, simulation, liveConsole);

    liveConsole.Init("━━━ Csomag kézbesítési szimuláció ━━━", couriers.Count);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    liveConsole.LogEvent("start", "Szimuláció elindult!");

    SimResult simResult;
    try
    {
        simResult = await orchestrator.RunAsync(couriers, orders, cts.Token);
        liveConsole.LogEvent("done", $"Kész! {simResult.Delivered}/{simResult.Total} kézbesítve");

        // Futási eredmény elmentése az adatbázisba
        await ResultSaver.SaveAsync(db, city.Id, simResult, orders, couriers);
        liveConsole.LogEvent("saved", "💾 Futás elmentve az adatbázisba.");
    }
    catch (OperationCanceledException)
    {
        liveConsole.Finish();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n  Szimuláció megszakítva.");
        Console.ResetColor();
        return;
    }

    liveConsole.Finish();
    PrintSummaryAndReports(simResult, orders, couriers);
}

// Új rendelés hozzáadása

static async Task AddOrderAsync(AppDbContext db)
{
    var city = await SelectCityAsync(db, "Rendelés hozzáadása — válassz várost");
    if (city == null) return;

    Console.Clear();
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"━━━ Új rendelés — {city.Name} ━━━━━━━━━━━━━━━━━━━━━━━━");
    Console.ResetColor();
    Console.WriteLine();

    Console.Write("Ügyfél neve: ");
    var customer = Console.ReadLine() ?? "";

    Console.Write("Cím (szabad szöveg): ");
    var address = Console.ReadLine() ?? "";

    var deliveryNodes = await db.Nodes
        .Where(n => n.CityId == city.Id && n.Type == "Delivery")
        .OrderBy(n => n.ZoneId)
        .ThenBy(n => n.Name)
        .ToListAsync();

    if (deliveryNodes.Count == 0)
    {
        Console.WriteLine("Nincs egyetlen 'Delivery' típusú hely sem ebben a városban.");
        return;
    }

    var nodeItems = deliveryNodes
        .Select(n => $"{n.Name}  (zóna: {n.ZoneId?.ToString() ?? "-"})")
        .ToList();

    int nodeIndex = ConsoleMenu.Show("Cél hely kiválasztása", nodeItems);
    if (nodeIndex < 0) return;

    var node = deliveryNodes[nodeIndex];
    int zoneId = node.ZoneId ?? 0;

    // ── Rendelésszám generálása a város stílusa szerint ──
    // Ha még nincs egyetlen rendelés sem a városhoz, fallback: "ORD-001"

    var existingNumbers = await db.Orders
        .Where(o => o.CityId == city.Id)
        .Select(o => o.Number)
        .ToListAsync();

    string prefix = "ORD";
    int maxNum = 0;
    int padding = 3;

    foreach (var num in existingNumbers)
    {
        int dash = num.LastIndexOf('-');
        if (dash < 0) continue;

        string p = num[..dash];
        string n = num[(dash + 1)..];
        if (!int.TryParse(n, out int parsed)) continue;

        prefix = p;
        padding = Math.Max(padding, n.Length);
        if (parsed > maxNum) maxNum = parsed;
    }

    // Mentés ideiglenes számmal, majd frissítés az auto-generált Id ismeretében.
    // Az Id-t NEM állítjuk be kézzel — a SQLite auto-increment kezeli,
    // hogy elkerüljük a UNIQUE constraint hibát.
    var order = new Order
    {
        CityId = city.Id,
        Number = "TMP",
        Customer = customer,
        Address = address,
        AddressNodeId = node.Id,
        ZoneId = zoneId
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();   // itt kapja meg az auto-generált Id-t

    order.Number = $"{prefix}-{(maxNum + 1).ToString().PadLeft(padding, '0')}";
    await db.SaveChangesAsync();

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  Hozzáadva: {order.Number} | {order.Customer} → {node.Name} (zóna {zoneId})");
    Console.ResetColor();
}

// Helyek listázása

static async Task ListPlacesAsync(AppDbContext db)
{
    var city = await SelectCityAsync(db, "Helyek listázása — válassz várost");
    if (city == null) return;

    Console.Clear();
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"━━━ Helyek — {city.Name} ━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    Console.ResetColor();
    Console.WriteLine();

    var nodes = await db.Nodes
        .Where(n => n.CityId == city.Id)
        .OrderBy(n => n.Type)
        .ThenBy(n => n.Name)
        .ToListAsync();

    Console.WriteLine(" Id  Típus       Zóna   Név");
    Console.WriteLine("──────────────────────────────────────────────");
    foreach (var n in nodes)
        Console.WriteLine($"{n.Id,3}  {n.Type,-10}  {n.ZoneId,4}   {n.Name}");
}

// Korábbi futtatások

static async Task ShowPastRunsAsync(AppDbContext db)
{
    var city = await SelectCityAsync(db, "Korábbi futások — válassz várost");
    if (city == null) return;

    while (true)
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"━━━ Korábbi futások — {city.Name} ━━━━━━━━━━━━━━━━━━━━");
        Console.ResetColor();
        Console.WriteLine();

        var runs = await db.Set<SimulationRun>()
            .Where(r => r.CityId == city.Id)
            .OrderByDescending(r => r.RunAt)
            .Take(20)
            .ToListAsync();

        if (runs.Count == 0)
        {
            Console.WriteLine("  Még nincs elmentett futás ehhez a városhoz.");
            return;
        }

        // Menüpontok a futásokból
        var items = runs.Select(r =>
            $"{r.RunAt:yyyy-MM-dd HH:mm}  |  " +
            $"OK: {r.Delivered}/{r.Total}  " +
            $"Késő: {r.Late}  " +
            $"Kézbesítetlen: {r.Unassigned}  " +
            $"({r.ElapsedSeconds:F1}s)"
        ).ToList();

        int index = ConsoleMenu.Show("Válassz egy futást a részletekhez (ESC = vissza)", items);
        if (index < 0) return;

        await ShowRunDetailsAsync(db, runs[index]);
    }
}

static async Task ShowRunDetailsAsync(AppDbContext db, SimulationRun run)
{
    Console.Clear();
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"━━━ Futás részletei — {run.RunAt:yyyy-MM-dd HH:mm} ━━━━━━━━━━━━━━━━");
    Console.ResetColor();
    Console.WriteLine();

    // Összesítő fejléc
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  Összes: {run.Total}  |  " +
                      $"Kézbesítve: {run.Delivered}  |  " +
                      $"Késő: {run.Late}  |  " +
                      $"Kézbesítetlen: {run.Unassigned}  |  " +
                      $"Idő: {run.ElapsedSeconds:F1}s");
    Console.ResetColor();
    Console.WriteLine();

    var logs = await db.DeliveryLogs
        .Where(l => l.SimRunId == run.Id)
        .OrderBy(l => l.CourierName)
        .ThenBy(l => l.OrderNumber)
        .ToListAsync();

    if (logs.Count == 0)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Nincsenek részletes logok ehhez a futáshoz.");
        Console.ResetColor();
    }
    else
    {
        // Fejléc
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  {"Rendelés",-12} {"Ügyfél",-25} {"Futár",-25} {"Kézb.",-7} {"Késő",-6} {"Ideális",-9} {"Tényleges"}");
        Console.WriteLine($"  {"─────────────────────────────────────────────────────────────────────────────"}");
        Console.ResetColor();

        foreach (var log in logs)
        {
            // Státusz szín
            if (!log.WasDelivered)
                Console.ForegroundColor = ConsoleColor.Red;
            else if (log.WasLate)
                Console.ForegroundColor = ConsoleColor.Yellow;
            else
                Console.ForegroundColor = ConsoleColor.Green;

            string delivered = log.WasDelivered ? "✔" : "✘";
            string late = log.WasLate ? "⚠" : "-";
            string courier = log.CourierName ?? "–";
            string ideal = log.IdealMinutes.HasValue ? $"{log.IdealMinutes}p" : "–";
            string actual = log.ActualMinutes.HasValue ? $"{log.ActualMinutes}p" : "–";

            Console.WriteLine($"  {log.OrderNumber,-12} {log.Customer,-20} {courier,-15} {delivered,-7} {late,-6} {ideal,-9} {actual}");
        }

        Console.ResetColor();
        Console.WriteLine();

        // Mini összesítő a logokból
        int okCount = logs.Count(l => l.WasDelivered && !l.WasLate);
        int lateCount = logs.Count(l => l.WasDelivered && l.WasLate);
        int failCount = logs.Count(l => !l.WasDelivered);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"Sikeres: {okCount}  ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"Késő: {lateCount}  ");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Nem kézbesítve: {failCount}");
        Console.ResetColor();
    }

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write("  Nyomj meg egy billentyűt a visszalépéshez...");
    Console.ResetColor();
    Console.ReadKey(intercept: true);
}

// Segédfüggvények

static async Task<City?> SelectCityAsync(AppDbContext db, string? title = null)
{
    var cities = await db.Cities.OrderBy(c => c.Name).ToListAsync();
    if (cities.Count == 0)
    {
        Console.WriteLine("Nincs egyetlen város sem az adatbázisban.");
        return null;
    }

    int index = ConsoleMenu.Show(title ?? "Város választása", cities.Select(c => c.Name).ToList());
    if (index < 0) return null;
    return cities[index];
}

static void PrintSetupScreen()
{
    Console.Clear();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("━━━ Csomag kézbesítés szimuláció ━━━");
    Console.ResetColor();
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine("━━━ ADATOK BETÖLTÉSE ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    Console.ResetColor();
}

static void PrintStep(string label, string detail)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.Write("  ✔ ");
    Console.ResetColor();
    Console.Write($"{label,-20}");
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"  {detail}");
    Console.ResetColor();
}

static void PrintSummaryAndReports(SimResult result, List<Order> orders, List<Courier> couriers)
{
    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine("━━━ ÖSSZESÍTŐ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    Console.ResetColor();

    Console.WriteLine($"  Összes rendelés:   {result.Total}");

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"  Kézbesítve:        {result.Delivered} ({result.SuccessRate:P0})");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"  Késve kézbesítve:  {result.Late} ({result.LateRate:P0})");
    Console.ResetColor();

    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"  Nem kézbesítve:    {result.Unassigned}");
    Console.ResetColor();

    Console.WriteLine($"  Futásidő:          {result.Elapsed.TotalSeconds:F1}s");

    Reports.PrintDelayReport(orders, couriers);
    Reports.PrintCourierReport(couriers);
    Reports.PrintZoneReport(orders, couriers);
}
