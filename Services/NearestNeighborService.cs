using DeliverySimulator.Graph;
using DeliverySimulator.Database.Models;

namespace DeliverySimulator.Services;

/// <summary>
/// Nearest Neighbor heurisztikával optimalizálja a kézbesítési sorrendet.
/// Nem garantál optimális megoldást, de gyors közelítést ad (TSP közelítő).
/// </summary>
public class NearestNeighborService
{
    private readonly CityGraph _graph;

    public NearestNeighborService(CityGraph graph) => _graph = graph;

    /// <summary>
    /// Rendezi a rendeléseket úgy, hogy mindig a jelenlegi pozícióhoz
    /// legközelebbi következő megálló kerüljön sorra.
    /// </summary>
    /// <param name="startNodeId">A futár induló csúcsa.</param>
    /// <param name="orders">A kézbesítendő rendelések.</param>
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

            if (nearest == null) break; // el nem érhető rendelések maradtak

            // Kiválasztjuk és lépünk a következő pozícióra
            optimized.Add(nearest);
            remaining.Remove(nearest);
            currentNode = nearest.AddressNodeId;
        }

        optimized.AddRange(remaining); // el nem érhető rendelések a lista végére kerülnek
        return optimized;
    }
}
