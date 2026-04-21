using DeliverySimulator.Display;
using DeliverySimulator.Graph;
using DeliverySimulator.Models;

namespace DeliverySimulator.Services;

// ══════════════════════════════════════════════════════
//  KÉZBESÍTÉSI SZIMULÁCIÓ
//
//  Egy futár teljes kézbesítési körének szimulációja:
//    futár jelenlegi pozíció → raktár → csomag felvétel
//    → kézbesítési cím → kézbesítve
//
//  Menet közben:
//    - Forgalom véletlenszerűen változik
//    - Az élő konzol frissül (LiveConsole)
//    - Késés detektálás (tényleges > ideális × 1.1)
//    - Ha késett → "ügyfélértesítés" küldése
// ══════════════════════════════════════════════════════

public class DeliverySimulationService
{
    private readonly CityGraph    _graph;
    private readonly LiveConsole  _console;

    // Késési küszöb: 10%-os tolerancia
    private const double DelayThreshold = 1.10;

    // Szimulációs sebesség: ennyi ms = 1 perc az alkalmazásban
    private const int MsPerMinute = 400;

    public DeliverySimulationService(CityGraph graph, LiveConsole console)
    {
        _graph   = graph;
        _console = console;
    }

    /// <summary>
    /// Egy futár egy rendelésének teljes szimulációja.
    /// </summary>
    public async Task SimulateAsync(
        Courier courier,
        Order   order,
        CancellationToken ct = default)
    {
        int totalActual = 0;

        // ── 1. Legközelebbi raktár meghatározása ────────
        int warehouseId = _graph.FindNearestWarehouse(
            courier.CurrentNodeId,
            courier.ZoneIds);

        var warehouse = _graph.GetNode(warehouseId)!;

        // ── 2. Futár → Raktár ───────────────────────────
        if (courier.CurrentNodeId != warehouseId)
        {
            _console.UpdateCourier(courier.Id, courier.Name,
                "🚗 raktárba tart", _graph.GetNode(courier.CurrentNodeId)?.Name ?? "?",
                warehouse.Name, courier.DeliveriesCompleted);

            _console.LogEvent("moving",
                $"{courier.Name} → raktárba: {warehouse.Name}");

            var (whPath, whTime) = _graph.FindShortestPath(courier.CurrentNodeId, warehouseId);
            await TraversePath(courier, whPath, ct);
            totalActual += whTime;
        }

        // ── 3. Csomag felvétel ──────────────────────────
        order.Status = OrderStatus.InTransit;

        _console.UpdateCourier(courier.Id, courier.Name,
            "📦 csomagot vesz fel", warehouse.Name,
            order.Address, courier.DeliveriesCompleted);

        _console.LogEvent("pickup",
            $"{courier.Name} felvette: {order.Number} ({order.Customer})");

        await Task.Delay(300, ct);  // rövid szünet a csomagfelvételhez

        // ── 4. Ideális idő kiszámítása (forgalom nélkül) ─
        int idealWh  = _graph.IdealTime(courier.CurrentNodeId, warehouseId);
        int idealDel = _graph.IdealTime(warehouseId, order.AddressNodeId);
        order.IdealMinutes = idealWh + idealDel;

        // ── 5. Raktár → Kézbesítési cím ─────────────────
        var destNode = _graph.GetNode(order.AddressNodeId);

        _console.UpdateCourier(courier.Id, courier.Name,
            "🚚 kézbesítés", warehouse.Name,
            destNode?.Name ?? order.Address,
            courier.DeliveriesCompleted,
            estimatedMin: idealDel);

        var (delivPath, delivTime) = _graph.FindShortestPath(warehouseId, order.AddressNodeId);
        await TraversePath(courier, delivPath, ct);
        totalActual += delivTime;

        // ── 6. Kézbesítés sikeres ───────────────────────
        order.Status        = OrderStatus.Delivered;
        order.ActualMinutes = totalActual;

        courier.CurrentNodeId = order.AddressNodeId;
        courier.AssignedOrderIds.Remove(order.Id);
        courier.DeliveriesCompleted++;
        courier.TotalTimeMinutes += totalActual;

        // ── 7. Késés ellenőrzés + értesítés ────────────
        bool late = totalActual > (order.IdealMinutes ?? 0) * DelayThreshold;

        if (late)
        {
            order.WasLate = true;
            courier.LateDeliveries++;

            int lateMins = totalActual - (order.IdealMinutes ?? 0);

            // ── KÉSÉS ÉRTESÍTÉS ──────────────────────────
            // Valós rendszerben: e-mail / SMS az ügyfélnek
            _console.LogEvent("delay",
                $"⚠ ÉRTESÍTÉS → {order.Customer} | {order.Number} " +
                $"| +{lateMins} perc késés");
        }
        else
        {
            _console.LogEvent("delivery",
                $"{courier.Name} → {order.Customer} ({order.Number}) " +
                $"| {totalActual} perc");
        }

        _console.UpdateCourier(courier.Id, courier.Name,
            "⏸ vár", destNode?.Name ?? "?",
            completedCount: courier.DeliveriesCompleted);
    }

    // ── Privát: útvonal bejárása lépésről lépésre ──────

    /// <summary>
    /// Szimulált mozgás egy útvonal mentén.
    /// Minden él bejárásánál: forgalom frissítés + késleltetés.
    /// </summary>
    private async Task TraversePath(Courier courier, List<int> path, CancellationToken ct)
    {
        for (int i = 0; i < path.Count - 1; i++)
        {
            ct.ThrowIfCancellationRequested();

            int from = path[i];
            int to   = path[i + 1];

            // Forgalom véletlenszerűen változik minden lépésnél
            _graph.UpdateTraffic();

            var edge = _graph.Edges.FirstOrDefault(e => e.From == from && e.To == to);
            int ms   = (edge?.CurrentMinutes ?? 1) * MsPerMinute;

            courier.CurrentNodeId = to;

            await Task.Delay(ms, ct);
        }
    }
}
