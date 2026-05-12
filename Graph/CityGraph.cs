using DeliverySimulator.Database.Models;
using System.Text.Json;

namespace DeliverySimulator.Graph;

public class CityGraph
{
    // Adatok
    public List<Node> Nodes { get; } = [];
    public List<Edge> Edges { get; } = [];

    private readonly Random _rng = new();

    public CityGraph(List<Node> nodes, List<Edge> edges)
    {
        Nodes.AddRange(nodes);
        Edges.AddRange(edges);
    }

    /// <summary>
    /// Legrövidebb útvonal keresése Dijkstra-val, aktuális forgalommal.
    /// Visszatér: (csúcs-lista, összesített perc).
    /// </summary>
    public (List<int> path, int totalMinutes) FindShortestPath(int from, int to)
    {
        var dist = new Dictionary<int, int>();
        var prev = new Dictionary<int, int>();
        var done = new HashSet<int>();

        foreach (var n in Nodes) { dist[n.Id] = int.MaxValue; prev[n.Id] = -1; }
        dist[from] = 0;

        int nodeCount = Nodes.Count;
        for (int step = 0; step < nodeCount - 1; step++)
        {
            // Legközelebbi nem feldolgozott csúcs
            int u = -1;
            int uDist = int.MaxValue;
            foreach (var kv in dist)
            {
                if (!done.Contains(kv.Key) && kv.Value < uDist)
                { u = kv.Key; uDist = kv.Value; }
            }

            if (u == -1 || uDist == int.MaxValue) break;
            done.Add(u);

            foreach (var edge in Edges.Where(e => e.From == u))
            {
                if (!dist.ContainsKey(edge.To)) continue;
                int newDist = dist[u] + edge.CurrentMinutes;
                if (newDist < dist[edge.To])
                {
                    dist[edge.To] = newDist;
                    prev[edge.To] = u;
                }
            }
        }

        // Útvonal visszakövetése
        var path = new List<int>();
        for (int cur = to; cur != -1 && prev.ContainsKey(cur); cur = prev[cur])
        {
            path.Insert(0, cur);
            if (cur == from) break;
        }
        if (path.Count == 0 || path[0] != from)
            return ([], int.MaxValue);

        return (path, dist.GetValueOrDefault(to, int.MaxValue));
    }

    public int IdealTime(int from, int to)
    {
        var dist = new Dictionary<int, int>();
        var done = new HashSet<int>();

        foreach (var n in Nodes) dist[n.Id] = int.MaxValue;
        dist[from] = 0;

        int nodeCount = Nodes.Count;
        for (int step = 0; step < nodeCount - 1; step++)
        {
            int u = -1; int uDist = int.MaxValue;
            foreach (var kv in dist)
            {
                if (!done.Contains(kv.Key) && kv.Value < uDist)
                { u = kv.Key; uDist = kv.Value; }
            }

            if (u == -1 || uDist == int.MaxValue) break;
            done.Add(u);

            foreach (var edge in Edges.Where(e => e.From == u))
            {
                if (!dist.ContainsKey(edge.To)) continue;
                int newDist = dist[u] + edge.IdealMinutes;
                if (newDist < dist[edge.To])
                    dist[edge.To] = newDist;
            }
        }

        return dist.GetValueOrDefault(to, int.MaxValue);
    }

    /// <summary>
    /// Véletlenszerű forgalomváltozás minden élen.
    /// Kis, szimmetrikus változás → átlag stabil marad.
    /// 5% esély: "baleset" → átmenetileg nagyobb torlódás.
    /// </summary>
    public void UpdateTraffic()
    {
        // Csak az egy irányú éleket dolgozzuk fel (From<To) hogy ne kettőzzük
        var uniqueEdges = Edges.Where(e => e.From < e.To).ToList();

        foreach (var edge in uniqueEdges)
        {
            double change;

            if (_rng.NextDouble() < 0.05)           // 5%: baleset
                change = 0.3 + _rng.NextDouble() * 0.2;
            else if (_rng.NextDouble() < 0.15)       // 15%: forgalom enyhül
                change = -(0.1 + _rng.NextDouble() * 0.1);
            else                                     // 80%: kis változás
                change = (_rng.NextDouble() - 0.5) * 0.1;

            // Mean reversion: ha már magas, nyomjuk le
            if (edge.TrafficMultiplier > 1.5) change -= 0.05;

            double next = Math.Clamp(edge.TrafficMultiplier + change, 0.7, 2.5);
            edge.TrafficMultiplier = next;

            // Visszairány ugyanannyi
            var reverse = Edges.FirstOrDefault(e => e.From == edge.To && e.To == edge.From);
            if (reverse != null) reverse.TrafficMultiplier = next;
        }
    }

    // ── Segédmetódusok ──────────────────────────────────

    public Node? GetNode(int id) => Nodes.FirstOrDefault(n => n.Id == id);

    /// <summary>
    /// Legközelebbi raktár-csúcs egy adott node-tól (Dijkstra szerint).
    /// </summary>
    public int FindNearestWarehouse(int fromNodeId, IEnumerable<int>? preferredZones = null)
    {
        var warehouses = Nodes.Where(n => n.Type == "Warehouse").ToList();

        // Ha van zóna-preferencia, előbb azokat próbáljuk
        if (preferredZones != null)
        {
            var zoneWarehouses = warehouses
                .Where(w => w.ZoneId.HasValue && preferredZones.Contains(w.ZoneId.Value))
                .ToList();
            if (zoneWarehouses.Count > 0)
                warehouses = zoneWarehouses;
        }

        int bestId = warehouses[0].Id;
        int bestTime = int.MaxValue;

        foreach (var wh in warehouses)
        {
            var (_, t) = FindShortestPath(fromNodeId, wh.Id);
            if (t < bestTime) { bestTime = t; bestId = wh.Id; }
        }

        return bestId;
    }
}
