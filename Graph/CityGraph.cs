using DeliverySimulator.Database.Models;
using System.Text.Json;

namespace DeliverySimulator.Graph;

public class CityGraph
{
    public CityGraph(List<Node> nodes, List<Edge> edges)
    {
        Nodes.AddRange(nodes);
        Edges.AddRange(edges);
    }
    // ── Adatok ─────────────────────────────────────────
    public List<Node> Nodes { get; } = [];
    public List<Edge> Edges { get; } = [];

    private readonly Random _rng = new();

    /// <summary>
    /// Gráf betöltése DB-ből a kiválasztott város alapján.
    /// </summary>
    public static CityGraph LoadFromDb(AppDbContext db, int cityId)
    {
        var graph = new CityGraph();

        var nodes = db.Nodes.Where(n => n.CityId == cityId).ToList();
        var edges = db.Edges.Where(e => e.CityId == cityId).ToList();

        // NodeEntity → Node (futásidős modell)
        // A JsonId az eredeti 0,1,2... azonosító amit a szimuláció használ
        foreach (var n in nodes)
            graph.Nodes.Add(new Node
            {
                Id = n.JsonId,   // ← a szimuláció ezt a számot várja
                Name = n.Name,
                Type = n.Zone,     // Zone-ba a "type" értéket mentettük (Warehouse/Delivery/Junction)
                ZoneId = n.ZoneId
            });

        // EdgeEntity → Edge (irányítatlan → mindkét irány)
        foreach (var e in edges)
        {
            graph.Edges.Add(new Edge { From = e.FromNodeId, To = e.ToNodeId, IdealMinutes = (int)e.Distance });
            graph.Edges.Add(new Edge { From = e.ToNodeId, To = e.FromNodeId, IdealMinutes = (int)e.Distance });
        }

        return graph;
    }

    // ── Dijkstra ────────────────────────────────────────

    /// <summary>
    /// Legrövidebb útvonal keresése Dijkstra-val, aktuális forgalommal.
    /// Visszatér: (csúcs-lista, összesített perc).
    /// </summary>
    public (List<int> path, int totalMinutes) FindShortestPath(int from, int to)
    {
        int n = Nodes.Count;
        var dist = new int[n];     // legrövidebb távolság
        var prev = new int[n];     // melyik csúcson át értük el
        var done = new bool[n];    // feldolgozva?

        // 1. Inicializálás
        for (int i = 0; i < n; i++) { dist[i] = int.MaxValue; prev[i] = -1; }
        dist[from] = 0;

        for (int step = 0; step < n - 1; step++)
        {
            // 2. Legközelebbi nem feldolgozott csúcs
            int u = -1;
            for (int i = 0; i < n; i++)
                if (!done[i] && (u == -1 || dist[i] < dist[u])) u = i;

            if (u == -1 || dist[u] == int.MaxValue) break;
            done[u] = true;

            // 3. Szomszédok frissítése
            foreach (var edge in Edges.Where(e => e.From == u))
            {
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
        for (int cur = to; cur != -1; cur = prev[cur])
            path.Insert(0, cur);

        if (path.Count == 0 || path[0] != from)
            return ([], int.MaxValue);

        return (path, dist[to]);
    }

    /// <summary>
    /// Ideális menetidő forgalom nélkül (összehasonlításhoz, késés-detektáláshoz).
    /// </summary>
    public int IdealTime(int from, int to)
    {
        // Ugyanaz mint FindShortestPath, de IdealMinutes-szal
        int n = Nodes.Count;
        var dist = new int[n];
        var done = new bool[n];

        for (int i = 0; i < n; i++) dist[i] = int.MaxValue;
        dist[from] = 0;

        for (int step = 0; step < n - 1; step++)
        {
            int u = -1;
            for (int i = 0; i < n; i++)
                if (!done[i] && (u == -1 || dist[i] < dist[u])) u = i;

            if (u == -1 || dist[u] == int.MaxValue) break;
            done[u] = true;

            foreach (var edge in Edges.Where(e => e.From == u))
            {
                int newDist = dist[u] + edge.IdealMinutes;   // IdealMinutes
                if (newDist < dist[edge.To])
                    dist[edge.To] = newDist;
            }
        }

        return dist[to];
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
