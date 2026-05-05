using System.Text.Json;
using DeliverySimulator.Display;
using DeliverySimulator.Graph;
using DeliverySimulator.Models;
using DeliverySimulator.Services;

// ══════════════════════════════════════════════════════
//  PROGRAM.CS  —  belépési pont
//
//  Feladata:
//    1. Adatok betöltése (JSON)
//    2. Service-ek összekapcsolása (wiring)
//    3. Szimuláció indítása
//    4. Riportok kiírása
//
// ══════════════════════════════════════════════════════

// ── 1. SETUP ─────────────────────────────────────────

PrintSetupScreen();

// Városgráf betöltése
var graph = CityGraph.LoadFromFile("Data/city.json");
PrintStep("Városgráf", $"{graph.Nodes.Count} csúcs, {graph.Edges.Count / 2} él");

// Futárok betöltése
var couriers = LoadJson<List<Courier>>("Data/couriers.json");
PrintStep("Futárok", $"{couriers.Count} futár betöltve");

// Rendelések betöltése
var orders = LoadJson<List<Order>>("Data/orders.json");
PrintStep("Rendelések", $"{orders.Count} rendelés betöltve");

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.Write("  Nyomj meg egy billentyűt a szimuláció indításához...");
Console.ResetColor();
Console.ReadKey(intercept: true);

// ── 2. SERVICE PÉLDÁNYOSÍTÁS ────────────────────────────────
// Objektumok létrehozása és összekapcsolása (SimOrch)

var liveConsole = new LiveConsole();

var greedy = new GreedyAssignmentService(graph);
var nn = new NearestNeighborService(graph);
var simulation = new DeliverySimulationService(graph, liveConsole);
var orchestrator = new SimulationOrchestrator(graph, greedy, nn, simulation, liveConsole);

// ── 3. SZIMULÁCIÓ ────────────────────────────────────

<<<<<<< HEAD
liveConsole.Init("━━━ Csomag kézbesítési szimuláció ━━━", couriers.Count);
=======
liveConsole.Init("Csomag kézbesítés szimuláció", couriers.Count);
>>>>>>> b3f4ccefe5b0979755247b56a6f7861ab75c8fe3

// Ctrl+C kezelése: leállítja a teljes folyamatot
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

liveConsole.LogEvent("start", "Szimuláció elindult!");

SimResult result;
try
{
    result = await orchestrator.RunAsync(couriers, orders, cts.Token);
    liveConsole.LogEvent("done",
        $"Kész! {result.Delivered}/{result.Total} kézbesítve");
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

// ── 4. RIPORTOK ──────────────────────────────────────

PrintSummaryAndReports(result, orders, couriers);

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.Write("  Nyomj meg egy billentyűt a kilépéshez...");
Console.ResetColor();
Console.ReadKey(intercept: true);


// ══════════════════════════════════════════════════════
//  SEGÉDFÜGGVÉNYEK (program szintű)
// ══════════════════════════════════════════════════════

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

/// <summary>
/// Egyszerű JSON betöltés generikusan.
/// </summary>
static T LoadJson<T>(string path)
{
    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<T>(json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new Exception($"Nem sikerült betölteni: {path}");
}
