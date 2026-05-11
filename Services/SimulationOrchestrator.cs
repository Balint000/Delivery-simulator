using System.Collections.Concurrent;
using System.Diagnostics;
using DeliverySimulator.Display;
using DeliverySimulator.Graph;
using DeliverySimulator.Database.Models;

namespace DeliverySimulator.Services;

public class SimulationOrchestrator
{
    private readonly CityGraph _graph;
    private readonly GreedyAssignmentService _greedy;
    private readonly NearestNeighborService _nn;
    private readonly DeliverySimulationService _sim;
    private readonly LiveConsole _console;

    // ── Élő queue referencia ──────────────────────────
    // RunAsync tölti fel, EnqueueOrder ezen keresztül ad hozzá rendelést futás közben.
    // volatile: a módosítás azonnal látható minden szálból, race condition nélkül.
    private volatile ConcurrentQueue<Order>? _liveQueue;

    public SimulationOrchestrator(
        CityGraph graph,
        GreedyAssignmentService greedy,
        NearestNeighborService nn,
        DeliverySimulationService sim,
        LiveConsole console)
    {
        _graph = graph;
        _greedy = greedy;
        _nn = nn;
        _sim = sim;
        _console = console;
    }

    // ── Élő rendelés hozzáadása ───────────────────────

    /// <summary>
    /// Futás közben ad hozzá egy új rendelést a szimulációhoz.
    ///
    /// A ConcurrentQueue-ba kerül — a futárok a következő Refill()
    /// hívásnál automatikusan felveszik, ha van szabad kapacitásuk
    /// és a megfelelő zónába esik.
    ///
    /// Ha a szimuláció még nem indult el, vagy már véget ért
    /// (_liveQueue == null), a rendelés figyelmen kívül marad.
    /// </summary>
    public void EnqueueOrder(Order order)
    {
        if (_liveQueue == null) return;

        order.Status = OrderStatus.Pending;
        _liveQueue.Enqueue(order);

        _console.LogEvent("refill",
            $"🆕 Új rendelés érkezett futás közben: {order.Number} ({order.Customer})");
    }

    /// <summary>
    /// Teljes szimuláció futtatása.
    /// </summary>
    public async Task<SimResult> RunAsync(
        List<Courier> couriers,
        List<Order> orders,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // ── 1. Initial batch assignment ─────────────────
        int assigned = _greedy.AssignAll(orders, couriers);
        _console.LogEvent("start",
            $"Hozzárendelve: {assigned}/{orders.Count} rendelés | " +
            $"Queue-ban: {orders.Count(o => o.Status == OrderStatus.Pending)}");

        // ── 2. Maradék → ConcurrentQueue ────────────────
        // _liveQueue-ként tároljuk, hogy EnqueueOrder elérhesse
        var queue = new ConcurrentQueue<Order>(
            orders.Where(o => o.Status == OrderStatus.Pending));

        _liveQueue = queue;

        var orderMap = orders.ToDictionary(o => o.Id);

        // ── 3. TPL: minden futár párhuzamosan indul ──────
        await Task.WhenAll(
            couriers.Select(c => CourierLoopAsync(c, queue, orderMap, ct)));

        // Szimuláció véget ért, queue-t nullázza
        _liveQueue = null;

        sw.Stop();

        // ── 4. Összesítés ────────────────────────────────
        // Az orders lista tartalmazza az eredeti rendeléseket.
        // Az élő hozzáadott rendelések az orderMap-ben nem szerepelnek,
        // de a queue-ból kerülnek kézbesítésre — külön összesíthetők.
        return new SimResult(
            Total: orders.Count,
            Delivered: orders.Count(o => o.Status == OrderStatus.Delivered),
            Late: orders.Count(o => o.WasLate),
            Unassigned: orders.Count(o => o.Status == OrderStatus.Pending),
            Elapsed: sw.Elapsed);
    }

    // ── Egy futár életciklusa (párhuzamosan fut) ────────

    private async Task CourierLoopAsync(
        Courier courier,
        ConcurrentQueue<Order> queue,
        Dictionary<int, Order> orderMap,
        CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batch = courier.AssignedOrderIds
                .ToList()
                .Select(id => orderMap.TryGetValue(id, out var o) ? o : null)
                .Where(o => o != null)
                .Cast<Order>()
                .ToList();

            if (batch.Count == 0)
            {
                batch = Refill(courier, queue, orderMap);
                if (batch.Count == 0) break;
            }

            var optimizedBatch = _nn.Optimize(courier.CurrentNodeId, batch);

            foreach (var order in optimizedBatch)
            {
                if (ct.IsCancellationRequested) break;
                await _sim.SimulateAsync(courier, order, ct);
            }

            if (!queue.IsEmpty)
                Refill(courier, queue, orderMap);

            if (courier.AssignedOrderIds.Count == 0 && queue.IsEmpty) break;
        }
    }

    /// <summary>
    /// Futár feltöltése a ConcurrentQueue-ból.
    ///
    /// FONTOS VÁLTOZÁS: az élőben hozzáadott rendelések nem szerepelnek
    /// az orderMap-ben. Ha TryGetValue sikertelen, akkor az order már
    /// tartalmaz minden szükséges adatot (a hívó állítja be).
    /// </summary>
    private List<Order> Refill(
        Courier courier,
        ConcurrentQueue<Order> queue,
        Dictionary<int, Order> orderMap)
    {
        var assigned = new List<Order>();
        var skipped = new List<Order>();
        int maxTries = queue.Count;

        while (assigned.Count < courier.FreeSlots && skipped.Count + assigned.Count < maxTries)
        {
            if (!queue.TryDequeue(out var order)) break;

            if (courier.CanServe(order.ZoneId))
            {
                order.Status = OrderStatus.Assigned;
                order.AssignedCourierId = courier.Id;
                courier.AssignedOrderIds.Add(order.Id);

                // Ha az orderMap nem tartalmazza (élő hozzáadás), felvesszük
                orderMap.TryAdd(order.Id, order);

                assigned.Add(order);

                _console.LogEvent("refill",
                    $"📥 {order.Number} → {courier.Name} (queue-ból)");
            }
            else
            {
                skipped.Add(order);
            }
        }

        foreach (var o in skipped) queue.Enqueue(o);
        return assigned;
    }
}

/// <summary>
/// A szimuláció végeredménye.
/// </summary>
public record SimResult(
    int Total,
    int Delivered,
    int Late,
    int Unassigned,
    TimeSpan Elapsed)
{
    public double SuccessRate => Total > 0 ? (double)Delivered / Total : 0;
    public double LateRate => Delivered > 0 ? (double)Late / Delivered : 0;
}
