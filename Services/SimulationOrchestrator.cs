using System.Collections.Concurrent;
using System.Diagnostics;
using DeliverySimulator.Display;
using DeliverySimulator.Graph;
using DeliverySimulator.Models;

namespace DeliverySimulator.Services;

// ══════════════════════════════════════════════════════
//  SZIMULÁCIÓ ORCHESTRÁTOR  —  a "karmester"
//
//  Felelőssége: a teljes szimuláció vezénylése.
//    1. Greedy: rendelések kiosztása futároknak (initial batch)
//    2. Maradék rendelések → ConcurrentQueue (szálbiztos sor)
//    3. Task.WhenAll: minden futár PÁRHUZAMOSAN dolgozik (TPL)
//    4. Minden futár kézbesítés után új rendelést kap a queue-ból
//
//  TPL (Task Parallel Library):
//    Task.WhenAll(tasks) → elindítja az összes feladatot egyszerre,
//    és megvárja amíg MINDENKI végzett.
//    Olyan, mint amikor egyszerre küldöd el az összes futárt,
//    nem egyik a másik után.
//
//  Thread-safety:
//    ConcurrentQueue.TryDequeue() → atomikus, több szál is
//    hívhatja egyszerre, nem adja ki ugyanazt az elemet kétszer.
// ══════════════════════════════════════════════════════

public class SimulationOrchestrator
{
    private readonly CityGraph                _graph;
    private readonly GreedyAssignmentService  _greedy;
    private readonly NearestNeighborService   _nn;
    private readonly DeliverySimulationService _sim;
    private readonly LiveConsole              _console;

    public SimulationOrchestrator(
        CityGraph graph,
        GreedyAssignmentService greedy,
        NearestNeighborService nn,
        DeliverySimulationService sim,
        LiveConsole console)
    {
        _graph   = graph;
        _greedy  = greedy;
        _nn      = nn;
        _sim     = sim;
        _console = console;
    }

    /// <summary>
    /// Teljes szimuláció futtatása.
    /// </summary>
    public async Task<SimResult> RunAsync(
        List<Courier> couriers,
        List<Order>   orders,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // ── 1. Initial batch assignment ─────────────────
        // Minden futárhoz MaxCapacity-ig rendelünk rendeléseket (greedy)
        int assigned = _greedy.AssignAll(orders, couriers);
        _console.LogEvent("start",
            $"Hozzárendelve: {assigned}/{orders.Count} rendelés | " +
            $"Queue-ban: {orders.Count(o => o.Status == OrderStatus.Pending)}");

        // ── 2. Maradék → ConcurrentQueue ────────────────
        // ConcurrentQueue: szálbiztos sor, több futár olvassa egyszerre
        var queue = new ConcurrentQueue<Order>(
            orders.Where(o => o.Status == OrderStatus.Pending));

        var orderMap = orders.ToDictionary(o => o.Id);

        // ── 3. TPL: minden futár párhuzamosan indul ──────
        //
        //  couriers.Select(c => CourierLoop(c, ...))
        //  → Task-ok listája (ígéretek a munkára)
        //
        //  Task.WhenAll(...)
        //  → Elindítja az összes Task-ot EGYSZERRE
        //  → Megvárja amíg mindenki végzett
        //
        // Szekvenciális változat lenne:
        //   foreach (var c in couriers) await CourierLoop(c, ...);
        //   → Az egyik megvárja a másikat → lassú!

        await Task.WhenAll(
            couriers.Select(c => CourierLoopAsync(c, queue, orderMap, ct)));

        sw.Stop();

        // ── 4. Összesítés ────────────────────────────────
        return new SimResult(
            Total:     orders.Count,
            Delivered: orders.Count(o => o.Status == OrderStatus.Delivered),
            Late:      orders.Count(o => o.WasLate),
            Unassigned:orders.Count(o => o.Status == OrderStatus.Pending),
            Elapsed:   sw.Elapsed);
    }

    // ── Egy futár életciklusa (párhuzamosan fut) ────────

    /// <summary>
    /// Egy futár teljes munkaciklusa.
    /// Addig fut, amíg van rendelés (saját batch + queue).
    /// </summary>
    private async Task CourierLoopAsync(
        Courier courier,
        ConcurrentQueue<Order> queue,
        Dictionary<int, Order> orderMap,
        CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Snapshot: .ToList() azért kell, mert a szimuláció
            // közben eltávolítja az elemet az AssignedOrderIds-ból
            // Ha közvetlenül iterálnánk rajta → InvalidOperationException!
            var batch = courier.AssignedOrderIds
                .ToList()
                .Select(id => orderMap[id])
                .ToList();

            // Ha üres → próbálunk a queue-ból tölteni
            if (batch.Count == 0)
            {
                batch = Refill(courier, queue, orderMap);
                if (batch.Count == 0) break;   // nincs több rendelés → vége
            }

            // Nearest Neighbor: optimális kézbesítési sorrend
            var optimizedBatch = _nn.Optimize(courier.CurrentNodeId, batch);

            foreach (var order in optimizedBatch)
            {
                if (ct.IsCancellationRequested) break;
                await _sim.SimulateAsync(courier, order, ct);
            }

            // Batch után: töltünk ha van még a queue-ban
            if (!queue.IsEmpty)
                Refill(courier, queue, orderMap);

            if (courier.AssignedOrderIds.Count == 0 && queue.IsEmpty) break;
        }
    }

    /// <summary>
    /// Futár feltöltése a ConcurrentQueue-ból.
    /// Csak a futár zónájába eső rendeléseket veszi fel.
    /// Rossz zónásakat visszateszi a sor végére.
    ///
    /// TryDequeue() → atomikus olvasás, szálbiztos
    /// </summary>
    private List<Order> Refill(
        Courier courier,
        ConcurrentQueue<Order> queue,
        Dictionary<int, Order> orderMap)
    {
        var assigned = new List<Order>();
        var skipped  = new List<Order>();
        int maxTries = queue.Count;

        while (assigned.Count < courier.FreeSlots && skipped.Count + assigned.Count < maxTries)
        {
            if (!queue.TryDequeue(out var order)) break;

            if (courier.CanServe(order.ZoneId))
            {
                order.Status             = OrderStatus.Assigned;
                order.AssignedCourierId  = courier.Id;
                courier.AssignedOrderIds.Add(order.Id);
                assigned.Add(order);

                _console.LogEvent("refill",
                    $"📥 {order.Number} → {courier.Name} (queue-ból)");
            }
            else
            {
                skipped.Add(order);  // más zóna → visszaadjuk
            }
        }

        foreach (var o in skipped) queue.Enqueue(o);
        return assigned;
    }
}

/// <summary>
/// A szimuláció végeredménye.
/// record = immutable adatosztály, automatikus ToString/Equals.
/// </summary>
public record SimResult(
    int      Total,
    int      Delivered,
    int      Late,
    int      Unassigned,
    TimeSpan Elapsed)
{
    public double SuccessRate => Total > 0 ? (double)Delivered / Total : 0;
    public double LateRate    => Delivered > 0 ? (double)Late / Delivered : 0;
}
