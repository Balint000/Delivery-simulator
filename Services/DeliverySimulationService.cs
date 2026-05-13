using DeliverySimulator.Display;
using DeliverySimulator.Graph;
using DeliverySimulator.Database.Models;

namespace DeliverySimulator.Services;

/// <summary>
/// Egyetlen rendelés fizikai kézbesítését szimulálja:
/// raktárba menet, csomagfelvétel, kézbesítés, késésellenőrzés.
/// </summary>
public class DeliverySimulationService
{
    private readonly CityGraph _graph;
    private readonly LiveConsole _console;

    // Tényleges idő ennél nagyobb arányban térhet el az ideálistól → késés
    private const double DelayThreshold = 1.10;

    // 1 szimulált perc = ennyi valós ms
    private const int MsPerMinute = 500;

    public DeliverySimulationService(CityGraph graph, LiveConsole console)
    {
        _graph = graph;
        _console = console;
    }

    /// <summary>
    /// Lefuttatja egy rendelés teljes kézbesítési folyamatát aszinkron módon.
    /// A futár pozíciója és statisztikái frissülnek a futás során.
    /// </summary>
    public async Task SimulateAsync(
        Courier courier,
        Order order,
        CancellationToken ct = default)
    {
        // Összegyűjtjük a tényleges (forgalommal terhelt) menetidőt
        int totalActual = 0;

        // 1. Legközelebbi raktár meghatározása
        int warehouseId = _graph.FindNearestWarehouse(
            courier.CurrentNodeId,
            courier.ZoneIds);

        var warehouse = _graph.GetNode(warehouseId)!;

        // 2. Futár → Raktár
        // Ha a futár már a raktárban van, kihagyjuk ezt a lépést
        if (courier.CurrentNodeId != warehouseId)
        {
            // Konzol frissítése: "raktárba tart" státusz
            _console.UpdateCourier(courier.Id, courier.Name,
                "[Raktárba tart]", _graph.GetNode(courier.CurrentNodeId)?.Name ?? "?",
                warehouse.Name, courier.DeliveriesCompleted);

            _console.LogEvent("moving",
                $"{courier.Name} → raktárba: {warehouse.Name}");

            // Dijkstra: legrövidebb út a jelenlegi pozíciótól a raktárig
            var (whPath, whTime) = _graph.FindShortestPath(courier.CurrentNodeId, warehouseId);

            // Lépésről lépésre bejárjuk az útvonalat, forgalommal
            await TraversePath(courier, whPath, ct);
            totalActual += whTime;
        }

        // 3. Csomag felvétel
        // Státusz váltás: a rendelés most úton van
        order.Status = OrderStatus.InTransit;

        _console.UpdateCourier(courier.Id, courier.Name,
            "[Csomag felvétel]", warehouse.Name,
            order.Address, courier.DeliveriesCompleted);

        _console.LogEvent("pickup",
            $"{courier.Name} felvette: {order.Number} ({order.Customer})");

        // Kis szünet a csomagfelvételhez (rakodási idő szimulációja)
        await Task.Delay(300, ct);

        // 4. Ideális menetidő kiszámítása
        int idealWh = _graph.IdealTime(courier.CurrentNodeId, warehouseId);
        int idealDel = _graph.IdealTime(warehouseId, order.AddressNodeId);
        order.IdealMinutes = idealWh + idealDel;

        // 5. Raktár → Kézbesítési cím
        var destNode = _graph.GetNode(order.AddressNodeId);

        // Konzol frissítése: "kézbesítés" státusz, ETA megjelenítése
        _console.UpdateCourier(courier.Id, courier.Name,
            "[Kézbesítés]", warehouse.Name,
            destNode?.Name ?? order.Address,
            courier.DeliveriesCompleted,
            estimatedMin: idealDel);

        // Dijkstra: legrövidebb út a raktártól a kézbesítési címig
        var (delivPath, delivTime) = _graph.FindShortestPath(warehouseId, order.AddressNodeId);

        // Útvonal bejárása forgalommal
        await TraversePath(courier, delivPath, ct);
        totalActual += delivTime;

        // 6. Kézbesítés sikeres
        order.Status = OrderStatus.Delivered;
        order.ActualMinutes = totalActual;

        // Futár pozíciójának frissítése: most a kézbesítési helyen van
        courier.CurrentNodeId = order.AddressNodeId;

        // Rendelés eltávolítása a futár aktív listájából
        courier.AssignedOrderIds.Remove(order.Id);

        // Statisztikák frissítése
        courier.DeliveriesCompleted++;
        courier.TotalTimeMinutes += totalActual;

        // 7. Késés ellenőrzés + értesítés
        bool late = totalActual > (order.IdealMinutes ?? 0) * DelayThreshold;

        if (late)
        {
            order.WasLate = true;
            courier.LateDeliveries++;

            // Késés mértéke percben
            int lateMins = totalActual - (order.IdealMinutes ?? 0);

            _console.LogNotification(order.Customer, order.Number, lateMins);
        }
        else
        {
            // Sikeres, időbeni kézbesítés
            _console.LogEvent("delivery",
                $"{courier.Name} → {order.Customer} ({order.Number}) " +
                $"| {totalActual} perc");
        }

        // Futár visszaáll "vár" státuszra a következő rendelésig
        _console.UpdateCourier(courier.Id, courier.Name,
            "[Vár]", destNode?.Name ?? "?",
            completedCount: courier.DeliveriesCompleted);
    }

    /// <summary>
    /// Egy teljes batch kézbesítése: egyszer megy raktárba, majd sorban kézbesít.
    /// A batch sorrendjét a hívó (NearestNeighbor) már optimalizálta.
    /// </summary>
    public async Task SimulateBatchAsync(
        Courier courier,
        List<Order> orders,
        CancellationToken ct = default)
    {
        if (orders.Count == 0) return;

        // 1. Raktárba megy egyszer az egész batch-hez
        int warehouseId = _graph.FindNearestWarehouse(courier.CurrentNodeId, courier.ZoneIds);
        var warehouse = _graph.GetNode(warehouseId)!;

        if (courier.CurrentNodeId != warehouseId)
        {
            _console.UpdateCourier(courier.Id, courier.Name,
                "[Raktárba tart]", _graph.GetNode(courier.CurrentNodeId)?.Name ?? "?",
                warehouse.Name, courier.DeliveriesCompleted);
            _console.LogEvent("moving", $"{courier.Name} → raktárba: {warehouse.Name}");

            var (whPath, _) = _graph.FindShortestPath(courier.CurrentNodeId, warehouseId);
            await TraversePath(courier, whPath, ct);
        }

        // 2. Összes csomag felvétele egyszerre
        foreach (var order in orders)
            order.Status = OrderStatus.InTransit;

        _console.LogEvent("pickup",
            $"{courier.Name} felvette: {orders.Count} csomag " +
            $"({string.Join(", ", orders.Select(o => o.Number))})");
        await Task.Delay(300, ct);

        // 3. Kézbesítés sorban — a sorrend már optimalizált
        foreach (var order in orders)
        {
            ct.ThrowIfCancellationRequested();

            var destNode = _graph.GetNode(order.AddressNodeId);

            // IdealMinutes: raktártól a célcímig (forgalom nélkül)
            order.IdealMinutes = _graph.IdealTime(warehouseId, order.AddressNodeId);

            int idealLeg = _graph.IdealTime(courier.CurrentNodeId, order.AddressNodeId);

            _console.UpdateCourier(courier.Id, courier.Name,
                "[Kézbesítés]", _graph.GetNode(courier.CurrentNodeId)?.Name ?? "?",
                destNode?.Name ?? order.Address,
                courier.DeliveriesCompleted,
                estimatedMin: idealLeg);

            var (delivPath, delivTime) = _graph.FindShortestPath(courier.CurrentNodeId, order.AddressNodeId);
            await TraversePath(courier, delivPath, ct);

            // Státusz és statisztikák frissítése
            order.Status = OrderStatus.Delivered;
            order.ActualMinutes = delivTime;
            courier.CurrentNodeId = order.AddressNodeId;
            courier.AssignedOrderIds.Remove(order.Id);
            courier.DeliveriesCompleted++;
            courier.TotalTimeMinutes += delivTime;

            // Késés-ellenőrzés
            bool late = delivTime > (order.IdealMinutes ?? 0) * DelayThreshold;
            if (late)
            {
                order.WasLate = true;
                courier.LateDeliveries++;
                int lateMins = delivTime - (order.IdealMinutes ?? 0);
                _console.LogNotification(order.Customer, order.Number, lateMins);
            }
            else
            {
                _console.LogEvent("delivery",
                    $"{courier.Name} → {order.Customer} ({order.Number}) | {delivTime} perc");
            }
        }

        _console.UpdateCourier(courier.Id, courier.Name,
            "[Vár]", _graph.GetNode(courier.CurrentNodeId)?.Name ?? "?",
            completedCount: courier.DeliveriesCompleted);
    }

    // Bejárja az útvonalat élről élre; minden lépésnél frissíti a forgalmat és vár.

    private async Task TraversePath(Courier courier, List<int> path, CancellationToken ct)
    {
        // path[0] = kiindulás, path[^1] = cél
        // Minden szomszédos pár egy élt jelent
        for (int i = 0; i < path.Count - 1; i++)
        {
            // CTRL + C ellenőrzés
            ct.ThrowIfCancellationRequested();

            int from = path[i];
            int to = path[i + 1];

            // Forgalom frissítése
            _graph.UpdateTraffic();

            // Megkeressük a konkrét él objektumot a menetidőhöz
            var edge = _graph.Edges.FirstOrDefault(e => e.From == from && e.To == to);

            int ms = (edge?.CurrentMinutes ?? 1) * MsPerMinute;

            // Futár pozíciójának frissítése
            courier.CurrentNodeId = to;

            // Aszinkron várakozás: 1 szimulált perc = MsPerMinute valós ms
            await Task.Delay(ms, ct);
        }
    }
}
