using DeliverySimulator.Graph;
using DeliverySimulator.Database.Models;

namespace DeliverySimulator.Services;

/// <summary>
/// Rendeléseket rendel hozzá futárokhoz a legközelebbi szabad futár alapján (mohó).
/// </summary>
public class GreedyAssignmentService
{
    private readonly CityGraph _graph;

    public GreedyAssignmentService(CityGraph graph) => _graph = graph;

    /// <summary>
    /// A legközelebbi, szabad, zónában dolgozó futárt rendeli a rendeléshez.
    /// Visszatér <c>null</c>-lal, ha nincs megfelelő futár.
    /// </summary>
    public Courier? AssignOne(Order order, List<Courier> couriers)
    {
        Courier? best = null;
        int bestTime = int.MaxValue;

        foreach (var courier in couriers)
        {
            // Szűrés
            if (!courier.HasRoom) continue;
            if (!courier.CanServe(order.ZoneId)) continue;

            // 1) Legközelebbi raktár a futár aktuális pozíciójához
            int warehouseId = _graph.FindNearestWarehouse(courier.CurrentNodeId, courier.ZoneIds);
            // 2) Idő futár -> raktár
            var (_, toWarehouseTime) = _graph.FindShortestPath(courier.CurrentNodeId, warehouseId);
            // 3) Idő raktár -> cím
            var (_, warehouseToAddressTime) = _graph.FindShortestPath(warehouseId, order.AddressNodeId);
            // 4) Összesített idő
            int totalTime = toWarehouseTime + warehouseToAddressTime;

            if (totalTime == int.MaxValue) continue;

            if (totalTime < bestTime)
            {
                bestTime = totalTime;
                best = courier;
            }
        }

        if (best != null)
        {
            order.Status = OrderStatus.Assigned;
            order.AssignedCourierId = best.Id;
            best.AssignedOrderIds.Add(order.Id);
        }

        return best;
    }

    /// <summary>
    /// Az összes <see cref="OrderStatus.Pending"/> rendelést hozzárendelése.
    /// Visszatér a sikeresen hozzárendelt rendelések számával.
    /// </summary>
    public int AssignAll(List<Order> orders, List<Courier> couriers)
    {
        int count = 0;
        foreach (var order in orders.Where(o => o.Status == OrderStatus.Pending))
        {
            if (AssignOne(order, couriers) != null) count++;
        }
        return count;
    }
}
