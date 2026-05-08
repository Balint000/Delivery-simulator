using DeliverySimulator.Graph;
using DeliverySimulator.Models;

namespace DeliverySimulator.Services;

public class GreedyAssignmentService
{
    private readonly CityGraph _graph;

    public GreedyAssignmentService(CityGraph graph) => _graph = graph;

    /// <summary>
    /// Egy rendeléshez megkeresi és hozzárendeli a legjobb futárt.
    ///
    /// Szűrési feltételek:
    ///   1. Van szabad hely (HasRoom)
    ///   2. Dolgozik ebben a zónában (CanServe)
    ///
    /// Kiválasztás: Dijkstra-távolság alapján a legközelebbi.
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

            // Dijkstra: futár jelenlegi pozíciója → rendelés célcsúcsa
            // 1) Legközelebbi raktár a futár aktuális pozíciójához
            int warehouseId = _graph.FindNearestWarehouse(courier.CurrentNodeId, courier.ZoneIds);

            // 2) Idő futár -> raktár
            var (_, toWarehouseTime) = _graph.FindShortestPath(courier.CurrentNodeId, warehouseId);

            // 3) Idő raktár -> cím
            var (_, warehouseToAddressTime) = _graph.FindShortestPath(warehouseId, order.AddressNodeId);

            // 4) Összesített idő: vissza telephelyre, onnan kiviszi
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
    /// Az összes Pending rendelés hozzárendelése (tömeges).
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
