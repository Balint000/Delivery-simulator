using System.Collections.Concurrent;
using System.Diagnostics;
using DeliverySimulator.Display;
using DeliverySimulator.Graph;
using DeliverySimulator.Database.Models;

namespace DeliverySimulator.Services;

/// <summary>
/// A teljes szimuláció vezérlője;
/// hozzárendelés-párhuzamosFutárok-összesítés
/// </summary>
public class ParallelRunner
{
    private readonly CityGraph _graph;
    private readonly GreedyAssignmentService _greedy;
    private readonly NearestNeighborService _nn;
    private readonly DeliverySimulationService _sim;
    private readonly LiveConsole _console;

    public ParallelRunner(
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

    /// <summary>
    /// Futtatja a szimulációt;
    /// (greedy hozzárendelés → párhuzamos futárhurok → összesítés)
    /// </summary>
    public async Task<SimResult> RunAsync(
        List<Courier> couriers,
        List<Order> orders,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // 1. Inicializálás
        int assigned = _greedy.AssignAll(orders, couriers);
        _console.LogEvent("start",
            $"Hozzárendelve: {assigned}/{orders.Count} rendelés | " +
            $"Queue-ban: {orders.Count(o => o.Status == OrderStatus.Pending)}");

        // 2.1 Maradék rendelés → ConcurrentQueue
        var queue = new ConcurrentQueue<Order>(
            orders.Where(o => o.Status == OrderStatus.Pending));

        var orderMap = orders.ToDictionary(o => o.Id);

        // 2.2 TPL
        await Task.WhenAll(couriers.Select(c => CourierLoopAsync(c, queue, orderMap, ct)));

        sw.Stop();

        // 4. Összesítés
        return new SimResult(
            Total: orders.Count,
            Delivered: orders.Count(o => o.Status == OrderStatus.Delivered),
            Late: orders.Count(o => o.WasLate),
            Unassigned: orders.Count(o => o.Status == OrderStatus.Pending),
            Elapsed: sw.Elapsed);
    }

    /// <summary>
    /// Egy futár ciklusa;
    /// batch feldolgozás, queue-feltöltés, leállás
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

            // .ToList() azért kell, mert a szimuláció
            // közben eltávolítja az elemet az AssignedOrderIds-ból
            var batch = courier.AssignedOrderIds
                .ToList()
                .Select(id => orderMap[id])
                .ToList();

            if (batch.Count == 0)
            {
                batch = Refill(courier, queue, orderMap);
                if (batch.Count == 0) break;
            }

            // Nearest Neighbor: optimális kézbesítési sorrend
            var optimizedBatch = _nn.Optimize(courier.CurrentNodeId, batch);

            if (!ct.IsCancellationRequested) await _sim.SimulateBatchAsync(courier, optimizedBatch, ct);

            //foreach (var order in optimizedBatch)
            // {
            //    if (ct.IsCancellationRequested) break;
            //    await _sim.SimulateAsync(courier, order, ct);
            // }

            if (!queue.IsEmpty) Refill(courier, queue, orderMap);

            if (courier.AssignedOrderIds.Count == 0 && queue.IsEmpty) break;
        }
    }

    /// <summary>
    /// A futár szabad kapacitását tölti fel a várakozó sorból.
    /// Más zónás rendeléseket visszatesz a sor végére.
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
                assigned.Add(order);

                _console.LogEvent("refill", $"📥 {order.Number} → {courier.Name} (queue-ból)");
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
/// A szimuláció összesített végeredménye.
/// </summary
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
