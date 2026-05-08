using DeliverySimulator.Graph;
using DeliverySimulator.Models;

namespace DeliverySimulator.Services;

public class NearestNeighborService
{
    private readonly CityGraph _graph;

    public NearestNeighborService(CityGraph graph) => _graph = graph;

    /// <summary>
    /// Rendelések optimális sorrendbe rendezése Nearest Neighbor-rel.
    /// </summary>
    /// <param name="startNodeId">Kiindulási csúcs (raktár)</param>
    /// <param name="orders">Kézbesítendő rendelések</param>
    /// <returns>Optimalizált sorrendű lista</returns>
    public List<Order> Optimize(int startNodeId, List<Order> orders)
    {
        // 0 vagy 1 rendelés → nincs mit rendezni
        if (orders.Count <= 1) return orders;

        var remaining = new List<Order>(orders);
        var optimized = new List<Order>();
        int currentNode = startNodeId;

        while (remaining.Count > 0)
        {
            // Megkeressük a legközelebbi rendelést
            Order? nearest = null;
            int nearestTime = int.MaxValue;

            foreach (var order in remaining)
            {
                var (_, time) = _graph.FindShortestPath(currentNode, order.AddressNodeId);
                if (time < nearestTime)
                {
                    nearestTime = time;
                    nearest = order;
                }
            }

            if (nearest == null) break;

            // Kiválasztjuk és lépünk a következő pozícióra
            optimized.Add(nearest);
            remaining.Remove(nearest);
            currentNode = nearest.AddressNodeId;
        }

        // Ha maradtak el nem érhető rendelések, fűzzük a végére
        optimized.AddRange(remaining);
        return optimized;
    }
}
