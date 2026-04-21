using DeliverySimulator.Graph;
using DeliverySimulator.Models;

namespace DeliverySimulator.Services;

// ══════════════════════════════════════════════════════
//  NEAREST NEIGHBOR ÚTVONAL-OPTIMALIZÁLÁS
//
//  Probléma: ha egy futárnak 3 rendelése van, milyen sorrendben
//  kézbesítsen? Véletlenszerű sorrend = felesleges kerülők.
//
//  Nearest Neighbor (közelítő TSP-megoldás):
//    1. Indulj a raktárból
//    2. Melyik rendelés a legközelebb? → azt kézbesítsd először
//    3. Onnan melyik a legközelebb? → azt másodiknak
//    4. Ismételd, amíg van rendelés
//
//  Nem garantál optimumot (NP-nehéz), de mindig jobb
//  vagy egyenlő a véletlen sorrendnél.
// ══════════════════════════════════════════════════════

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
        if (orders.Count <= 1) return new List<Order>(orders);

        var remaining  = new List<Order>(orders);
        var optimized  = new List<Order>();
        int currentNode = startNodeId;

        while (remaining.Count > 0)
        {
            // Megkeressük a legközelebbi rendelést
            Order? nearest   = null;
            int    nearestTime = int.MaxValue;

            foreach (var order in remaining)
            {
                var (_, time) = _graph.FindShortestPath(currentNode, order.AddressNodeId);
                if (time < nearestTime)
                {
                    nearestTime = time;
                    nearest     = order;
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
