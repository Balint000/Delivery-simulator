using DeliverySimulator.Display;
using DeliverySimulator.Graph;
using DeliverySimulator.Models;

namespace DeliverySimulator.Services;

// ══════════════════════════════════════════════════════
//  KÉZBESÍTÉSI SZIMULÁCIÓ
//
//  Egy futár teljes kézbesítési körének szimulációja:
//    futár jelenlegi pozíció → raktár → csomag felvétel
//    → kézbesítési cím → kézbesítve
//
//  Menet közben:
//    - Forgalom véletlenszerűen változik (UpdateTraffic)
//    - Az élő konzol frissül (LiveConsole)
//    - Késés detektálás: tényleges > ideális × 1.10
//    - Ha késett:
//        → LogEvent("delay", ...) rövid jelzés az Eseményekbe
//        → LogNotification(...)   részletes értesítés a megrendelőnek  ÚJ
// ══════════════════════════════════════════════════════

public class DeliverySimulationService
{
    private readonly CityGraph _graph;
    private readonly LiveConsole _console;

    // Késési küszöb: 10%-os tolerancia.
    // Ha a tényleges idő > ideális × 1.10, akkor "késett".
    // Pl. ideális 10 perc → csak 11+ perctől számít késésnek.
    private const double DelayThreshold = 1.10;

    // Szimulációs sebesség: ennyi valós ms felel meg 1 szimulált percnek.
    // Csökkentésével gyorsítható a szimuláció (pl. 100 ms = gyors teszt).
    private const int MsPerMinute = 400;

    public DeliverySimulationService(CityGraph graph, LiveConsole console)
    {
        _graph = graph;
        _console = console;
    }

    /// <summary>
    /// Egy futár egyetlen rendelésének teljes aszinkron szimulációja.
    ///
    /// A metódus végigmegy a teljes kézbesítési folyamaton:
    ///   1. Raktár megkeresése
    ///   2. Mozgás a raktárhoz
    ///   3. Csomag felvétel
    ///   4. Ideális menetidő kiszámítása (összehasonlítási alap)
    ///   5. Mozgás a kézbesítési címre
    ///   6. Kézbesítés rögzítése + statisztikák frissítése
    ///   7. Késés ellenőrzése + értesítés
    /// </summary>
    /// <param name="courier">A kézbesítést végző futár</param>
    /// <param name="order">A kézbesítendő rendelés</param>
    /// <param name="ct">Lemondási token (Ctrl+C kezeléshez)</param>
    public async Task SimulateAsync(
        Courier courier,
        Order order,
        CancellationToken ct = default)
    {
        // Összegyűjtjük a tényleges (forgalommal terhelt) menetidőt
        int totalActual = 0;

        // ── 1. Legközelebbi raktár meghatározása ────────
        // A futár zónáit figyelembe vesszük: előnyben részesítjük
        // a saját zónájában lévő raktárt (FindNearestWarehouse belső logikája)
        int warehouseId = _graph.FindNearestWarehouse(
            courier.CurrentNodeId,
            courier.ZoneIds);

        var warehouse = _graph.GetNode(warehouseId)!;

        // ── 2. Futár → Raktár ───────────────────────────
        // Ha a futár már a raktárban van, kihagyjuk ezt a lépést
        if (courier.CurrentNodeId != warehouseId)
        {
            // Konzol frissítése: "raktárba tart" státusz
            _console.UpdateCourier(courier.Id, courier.Name,
                "🚗 raktárba tart", _graph.GetNode(courier.CurrentNodeId)?.Name ?? "?",
                warehouse.Name, courier.DeliveriesCompleted);

            _console.LogEvent("moving",
                $"{courier.Name} → raktárba: {warehouse.Name}");

            // Dijkstra: legrövidebb út a jelenlegi pozíciótól a raktárig
            var (whPath, whTime) = _graph.FindShortestPath(courier.CurrentNodeId, warehouseId);

            // Lépésről lépésre bejárjuk az útvonalat, forgalommal
            await TraversePath(courier, whPath, ct);
            totalActual += whTime;
        }

        // ── 3. Csomag felvétel ──────────────────────────
        // Státusz váltás: a rendelés most úton van
        order.Status = OrderStatus.InTransit;

        _console.UpdateCourier(courier.Id, courier.Name,
            "📦 csomagot vesz fel", warehouse.Name,
            order.Address, courier.DeliveriesCompleted);

        _console.LogEvent("pickup",
            $"{courier.Name} felvette: {order.Number} ({order.Customer})");

        // Kis szünet a csomagfelvételhez (valós rakodási idő szimulációja)
        await Task.Delay(300, ct);

        // ── 4. Ideális menetidő kiszámítása ─────────────
        // IdealTime() forgalom NÉLKÜL fut — ez az összehasonlítási alap.
        // Ha a tényleges idő ennél >10%-kal több → késés.
        int idealWh = _graph.IdealTime(courier.CurrentNodeId, warehouseId);
        int idealDel = _graph.IdealTime(warehouseId, order.AddressNodeId);
        order.IdealMinutes = idealWh + idealDel;

        // ── 5. Raktár → Kézbesítési cím ─────────────────
        var destNode = _graph.GetNode(order.AddressNodeId);

        // Konzol frissítése: "kézbesítés" státusz, ETA megjelenítése
        _console.UpdateCourier(courier.Id, courier.Name,
            "🚚 kézbesítés", warehouse.Name,
            destNode?.Name ?? order.Address,
            courier.DeliveriesCompleted,
            estimatedMin: idealDel);

        // Dijkstra: legrövidebb út a raktártól a kézbesítési címig
        var (delivPath, delivTime) = _graph.FindShortestPath(warehouseId, order.AddressNodeId);

        // Útvonal bejárása forgalommal (ez ad késést ha torlódás van)
        await TraversePath(courier, delivPath, ct);
        totalActual += delivTime;

        // ── 6. Kézbesítés sikeres ───────────────────────
        order.Status = OrderStatus.Delivered;
        order.ActualMinutes = totalActual;

        // Futár pozíciójának frissítése: most a kézbesítési helyen van
        courier.CurrentNodeId = order.AddressNodeId;

        // Rendelés eltávolítása a futár aktív listájából
        courier.AssignedOrderIds.Remove(order.Id);

        // Statisztikák frissítése
        courier.DeliveriesCompleted++;
        courier.TotalTimeMinutes += totalActual;

        // ── 7. Késés ellenőrzés + értesítés ────────────
        // Késett-e: tényleges idő meghaladja-e az ideális × küszöb értéket?
        bool late = totalActual > (order.IdealMinutes ?? 0) * DelayThreshold;

        if (late)
        {
            order.WasLate = true;
            courier.LateDeliveries++;

            // Késés mértéke percben (ideálistól való eltérés)
            int lateMins = totalActual - (order.IdealMinutes ?? 0);

            // Rövid jelzés az Események panelbe (nem a részletes értesítés)
            // _console.LogEvent("delay",
            //    $"{order.Number} késik | {courier.Name} | +{lateMins}p");

            // ── ÉRTESÍTÉS a megrendelőnek ────────────────
            // ÚJ: külön panelban jelenik meg, nem az eseménynaplóban.
            // Valós rendszerben: itt küldene e-mailt / SMS-t az ügyfélnek.
            _console.LogNotification(order.Customer, order.Number, lateMins);
        }
        else
        {
            // Sikeres, időbeni kézbesítés — zöld esemény az eseménynaplóban
            _console.LogEvent("delivery",
                $"{courier.Name} → {order.Customer} ({order.Number}) " +
                $"| {totalActual} perc");
        }

        // Futár visszaáll "vár" státuszra a következő rendelésig
        _console.UpdateCourier(courier.Id, courier.Name,
            "⏸ vár", destNode?.Name ?? "?",
            completedCount: courier.DeliveriesCompleted);
    }

    // ── Privát: útvonal bejárása lépésről lépésre ──────

    /// <summary>
    /// Szimulált mozgás egy útvonal mentén, élről élre.
    ///
    /// Minden lépésnél (él bejárásánál):
    ///   1. UpdateTraffic(): forgalom véletlenszerűen változik
    ///   2. Task.Delay(): valós várakozás (forgalommal arányos)
    ///   3. courier.CurrentNodeId frissítése az új pozícióra
    ///
    /// A CancellationToken leállítja a mozgást ha Ctrl+C érkezik.
    /// </summary>
    /// <param name="courier">A mozgó futár (pozícióját frissítjük)</param>
    /// <param name="path">Csúcsok Id listája (Dijkstra adja vissza)</param>
    /// <param name="ct">Lemondási token</param>
    private async Task TraversePath(Courier courier, List<int> path, CancellationToken ct)
    {
        // path[0] = kiindulás, path[^1] = cél
        // Minden szomszédos pár egy élt jelent
        for (int i = 0; i < path.Count - 1; i++)
        {
            // Lemondás ellenőrzése minden él előtt
            ct.ThrowIfCancellationRequested();

            int from = path[i];
            int to = path[i + 1];

            // Forgalom frissítése: véletlenszerű változás minden lépésnél
            // Ez okozza a dinamikus késéseket a szimulációban
            _graph.UpdateTraffic();

            // Megkeressük a konkrét él objektumot a menetidőhöz
            var edge = _graph.Edges.FirstOrDefault(e => e.From == from && e.To == to);

            // CurrentMinutes = IdealMinutes × TrafficMultiplier (forgalommal terhelt)
            int ms = (edge?.CurrentMinutes ?? 1) * MsPerMinute;

            // Futár pozíciójának frissítése még a Delay előtt
            // (a konzol azonnal mutatja az új helyszínt)
            courier.CurrentNodeId = to;

            // Aszinkron várakozás: 1 szimulált perc = MsPerMinute valós ms
            await Task.Delay(ms, ct);
        }
    }
}
