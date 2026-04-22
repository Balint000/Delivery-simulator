# DeliverySimulator — Műszaki Dokumentáció

**Keretrendszer:** .NET 10.0
**Nyelv:** C# 13
**Típus:** Konzolos alkalmazás

---

## Tartalomjegyzék

1. [Architektúra áttekintés](#1-architektúra-áttekintés)
2. [Adatmodellek](#2-adatmodellek)
3. [Városgráf és navigáció](#3-városgráf-és-navigáció)
4. [Service réteg](#4-service-réteg)
5. [Megjelenítés](#5-megjelenítés)
6. [Adatfájlok sémája](#6-adatfájlok-sémája)
7. [Szimulációs folyamat](#7-szimulációs-folyamat)
8. [Hibakezelés és megszakítás](#8-hibakezelés-és-megszakítás)
9. [Teljesítmény és skálázhatóság](#9-teljesítmény-és-skálázhatóság)
10. [Ismert korlátok](#10-ismert-korlátok)

---

## 1. Architektúra áttekintés

A projekt egy egyszerű, rétegelt architektúrát követ:

```
┌─────────────────────────────────────┐
│           Program.cs                │  ← Belépési pont, DI/wiring
├─────────────┬───────────────────────┤
│  Display/   │      Services/        │  ← Prezentáció / Üzleti logika
│  LiveConsole│  Orchestrator         │
│  Reports    │  GreedyAssignment     │
│             │  NearestNeighbor      │
│             │  DeliverySimulation   │
├─────────────┴───────────────────────┤
│         Graph/CityGraph             │  ← Infrastruktúra (gráf, Dijkstra)
├─────────────────────────────────────┤
│         Models/Models.cs            │  ← Domain modellek
├─────────────────────────────────────┤
│         Data/*.json                 │  ← Adatforrás
└─────────────────────────────────────┘
```

A függőségek iránya mindig lefelé mutat; az alsóbb rétegek nem hivatkoznak a felsőkre. A `Program.cs` végzi a kézi dependency injection-t (nincs DI konténer).

---

## 2. Adatmodellek

**Névtér:** `DeliverySimulator.Models`  
**Fájl:** `Models/Models.cs`

### `Node` — Városgráf csúcs

| Property | Típus | Leírás |
|----------|-------|--------|
| `Id` | `int` | Egyedi azonosító (0-tól indexelt) |
| `Name` | `string` | Megjelenítési név |
| `Type` | `string` | `"Warehouse"` \| `"Delivery"` \| `"Junction"` |
| `ZoneId` | `int?` | Zóna azonosító; `null` ha Junction |

### `Edge` — Városgráf él

| Property | Típus | Leírás |
|----------|-------|--------|
| `From` | `int` | Forrás csúcs Id |
| `To` | `int` | Cél csúcs Id |
| `IdealMinutes` | `int` | Forgalom nélküli alap menetidő |
| `TrafficMultiplier` | `double` | Forgalmi szorzó (0.7–2.5) |
| `CurrentMinutes` | `int` *(computed)* | `IdealMinutes × TrafficMultiplier` |

> A gráf **irányítatlan éleket** tárol irányítottan: minden JSON él két bejegyzést hoz létre (`From→To` és `To→From`), azonos `TrafficMultiplier`-rel.

### `Courier` — Futár

| Property | Típus | Leírás |
|----------|-------|--------|
| `Id` | `int` | Egyedi azonosító |
| `Name` | `string` | Teljes név |
| `CurrentNodeId` | `int` | Jelenlegi pozíció (gráf-csúcs) |
| `ZoneIds` | `List<int>` | Kiszolgálható zónák |
| `MaxCapacity` | `int` | Egyszerre vihető csomagok max. száma |
| `AssignedOrderIds` | `List<int>` | Jelenleg hozzárendelt rendelések |
| `DeliveriesCompleted` | `int` | *(stat)* Teljesített kézbesítések |
| `LateDeliveries` | `int` | *(stat)* Késett kézbesítések |
| `TotalTimeMinutes` | `int` | *(stat)* Összesített menetidő |
| `HasRoom` | `bool` *(computed)* | `AssignedOrderIds.Count < MaxCapacity` |
| `FreeSlots` | `int` *(computed)* | Szabad helyek száma |
| `AvgTime` | `double` *(computed)* | Átlagos kézbesítési idő percben |

### `Order` — Rendelés

| Property | Típus | Leírás |
|----------|-------|--------|
| `Id` | `int` | Egyedi azonosító |
| `Number` | `string` | Rendelésszám (pl. `ORD-001`) |
| `Customer` | `string` | Ügyfél neve |
| `Address` | `string` | Cím szövege (csak megjelenítéshez) |
| `AddressNodeId` | `int` | Célcsúcs a gráfban |
| `ZoneId` | `int` | Zóna azonosító |
| `Status` | `OrderStatus` | Jelenlegi állapot |
| `AssignedCourierId` | `int?` | Hozzárendelt futár Id |
| `IdealMinutes` | `int?` | Forgalom nélküli ideális menetidő |
| `ActualMinutes` | `int?` | Tényleges (forgalommal mért) menetidő |
| `WasLate` | `bool` | Késett-e a kézbesítés |
| `LateMinutes` | `int` *(computed)* | Késés mértéke percben |

### `OrderStatus` enum

```
Pending   → Assigned   → InTransit   → Delivered
```

---

## 3. Városgráf és navigáció

**Névtér:** `DeliverySimulator.Graph`  
**Fájl:** `Graph/CityGraph.cs`

### Betöltés

A `CityGraph.LoadFromFile(path)` statikus metódus JSON-ból tölti be a gráfot. Az élek kétszer kerülnek be (mindkét irányban), így a Dijkstra-algoritmus irányítatlan gráfként kezeli őket.

### Dijkstra implementáció

Az alkalmazás két Dijkstra-változatot tartalmaz:

| Metódus | Élsúly | Cél |
|---------|--------|-----|
| `FindShortestPath(from, to)` | `CurrentMinutes` (forgalommal) | Navigáció |
| `IdealTime(from, to)` | `IdealMinutes` (forgalom nélkül) | Késés-számítás alapja |

Mindkettő `O(V²)` komplexitású (tömb alapú, prioritásos sor nélkül). A jelenlegi gráfméret (8 csúcs) mellett ez teljesen elegendő.

**Visszatérési érték:** `(List<int> path, int totalMinutes)` — a csúcsok Id listája és az összesített menetidő.

Ha a célcsúcs nem elérhető, a visszatérési értékben `path = []` és `totalMinutes = int.MaxValue`.

### Forgalomszimuláció

A `UpdateTraffic()` metódus minden él `TrafficMultiplier`-ét módosítja egy lépésenként:

| Esemény | Valószínűség | Változás |
|---------|-------------|---------|
| Baleset | 5% | +30–50% |
| Forgalom enyhül | 15% | −10–20% |
| Kis változás | 80% | ±5% |
| Mean reversion | ha > 1.5 | −5% korrekció |

A szorzó `[0.7, 2.5]` közé van korlátozva (`Math.Clamp`). Visszairányú élek mindig azonos szorzót kapnak, hogy szimmetrikus forgalom legyen.

### Segédmetódusok

- `GetNode(id)` — csúcs keresés Id alapján
- `FindNearestWarehouse(fromNodeId, preferredZones?)` — legközelebbi raktár Dijkstra-távolság alapján; ha van zóna-preferencia, előbb a zónás raktárakat próbálja

---

## 4. Service réteg

**Névtér:** `DeliverySimulator.Services`

### `GreedyAssignmentService`

Mohó hozzárendelési algoritmus. Minden rendeléshez megkeresi az optimális futárt:

**Szűrési feltételek:**
1. `courier.HasRoom == true` (van szabad hely)
2. `courier.CanServe(order.ZoneId) == true` (megfelelő zóna)

**Kiválasztás:** legkisebb Dijkstra-távolság a futár jelenlegi pozíciójától a rendelés célcsúcsáig.

```
AssignOne(order, couriers) → Courier?
AssignAll(orders, couriers) → int  (hozzárendelt rendelések száma)
```

### `NearestNeighborService`

Közelítő TSP-megoldás több csomag optimális kézbesítési sorrendjéhez.

**Algoritmus:**
1. Indulás a `startNodeId` csúcsból
2. Megkeresni a legközelebbi még nem kézbesített rendelést
3. Azt felvenni, pozíciót frissíteni
4. Ismételni, amíg van rendelés

Ha egy rendelés nem elérhető (pl. gráfban nincs út), a sor végére kerül.

```
Optimize(startNodeId, orders) → List<Order>
```

### `DeliverySimulationService`

Egy futár egyetlen rendelésének teljes szimulációja aszinkron módon.

**Kézbesítési folyamat:**

```
1. Legközelebbi raktár meghatározása (FindNearestWarehouse)
2. Futár mozgatása a raktárhoz (TraversePath)
3. Csomag felvétel (300ms delay + státusz frissítés)
4. Ideális menetidő kiszámítása (IdealTime)
5. Futár mozgatása a célcímre (TraversePath)
6. Kézbesítés rögzítése (státusz, statisztikák)
7. Késés ellenőrzése (actualMinutes > idealMinutes × 1.10)
8. Értesítés küldése ha késett
```

**`TraversePath` belső működése:**  
Lépésről lépésre bejárja az útvonalat. Minden él bejárásánál frissíti a forgalmat, majd `Task.Delay(edge.CurrentMinutes × MsPerMinute)` hívással szimulálja az utazási időt.

```
SimulateAsync(courier, order, ct) → Task
```

### `SimulationOrchestrator`

A teljes szimuláció koordinátora.

**`RunAsync` folyamat:**

```
1. Greedy initial batch: AssignAll(orders, couriers)
   → minden futárhoz MaxCapacity-ig rendel rendelést
   
2. Maradék Pending rendelések → ConcurrentQueue<Order>

3. Task.WhenAll(couriers.Select(c => CourierLoopAsync(c, queue, ...)))
   → minden futár párhuzamosan dolgozik

4. SimResult összesítés és visszatérés
```

**`CourierLoopAsync` futár életciklusa:**

```
while (van rendelés) {
    batch = aktuálisan hozzárendelt rendelések snapshot-ja
    
    if (batch üres) {
        batch = Refill(courier, queue)  // queue-ból töltés
        if (batch üres) break           // nincs több munka
    }
    
    optimizedBatch = NearestNeighbor.Optimize(batch)
    
    foreach (order in optimizedBatch)
        await SimulateAsync(courier, order)
    
    if (!queue.IsEmpty) Refill(courier, queue)
}
```

**`Refill` logika:**  
A `ConcurrentQueue`-ból próbál rendeléseket felvenni a futár szabad kapacitásáig. Rossz zónájú rendeléseket kihagyja és visszateszi a sor végére. A `maxTries` korlát (a queue aktuális mérete) megakadályozza a végtelen ciklust akkor, ha a futár egyetlen zónás rendelést sem talál.

---

## 5. Megjelenítés

**Névtér:** `DeliverySimulator.Display`

### `LiveConsole`

Thread-safe, pozíció-alapú konzol megjelenítő. `lock(_lock)` védi az összes konzolírási műveletet.

**Elvek:**
- `Console.SetCursorPosition(x, y)` + felülírás = "frissülési" effekt görgetés nélkül
- `Console.CursorVisible = false` az inicializáláskor, visszaállítás a `Finish()` híváskor
- Az eseménynapló FIFO-elvű, maximum `MaxEvents` (10) sort tárol

**Fő metódusok:**

| Metódus | Leírás |
|---------|--------|
| `Init(title, courierCount)` | Képernyő inicializálás, pozíciók rögzítése. **Csak egyszer hívható!** |
| `UpdateCourier(id, name, status, location, ...)` | Egy futár sorának frissítése |
| `LogEvent(type, message)` | Esemény hozzáadása a naplóhoz |
| `Finish()` | Kurzor és szín visszaállítása a szimuláció végén |

**Esemény ikonok:**

| Típus | Ikon |
|-------|------|
| `delivery` | ✅ |
| `delay` | ⚠️ |
| `moving` | 🚗 |
| `pickup` | 📦 |
| `start` | 🚀 |
| `done` | 🏁 |
| `refill` | 📥 |

### `Reports`

Statikus osztály, három statisztikai riportot jelenít meg a szimuláció végén.

| Metódus | Leírás |
|---------|--------|
| `PrintDelayReport(orders, couriers)` | Késett rendelések listája |
| `PrintCourierReport(couriers)` | Futár teljesítmény rangsor |
| `PrintZoneReport(orders, couriers)` | Zónánkénti terhelési statisztika |

---

## 6. Adatfájlok sémája

### `Data/city.json`

```jsonc
{
  "cityName": "string",          // Város neve (csak megjelenítéshez)
  "nodes": [
    {
      "id": 0,                   // int, egyedi, 0-tól indexelt
      "name": "string",
      "type": "Warehouse|Delivery|Junction",
      "zoneId": 1                // int | null (Junction esetén null)
    }
  ],
  "edges": [
    {
      "from": 0,                 // forrás csúcs id
      "to": 1,                   // cél csúcs id
      "idealTimeMinutes": 5      // pozitív int
    }
  ]
}
```

> **Megjegyzés:** Az élek irányítatlanok — a `from→to` és `to→from` irányokat a betöltő automatikusan hozzáadja.

### `Data/couriers.json`

```jsonc
[
  {
    "id": 1,                     // int, egyedi
    "name": "string",
    "currentNodeId": 0,          // kezdeti pozíció (létező csúcs id)
    "zoneIds": [1, 2],           // kiszolgálható zónák listája
    "maxCapacity": 3             // pozitív int
  }
]
```

### `Data/orders.json`

```jsonc
[
  {
    "id": 1,                     // int, egyedi
    "number": "ORD-001",         // string, megjelenítési azonosító
    "customer": "string",
    "address": "string",         // szöveges cím (csak megjelenítéshez)
    "addressNodeId": 1,          // célcsúcs (létező csúcs id)
    "zoneId": 1                  // int, a futár szűréséhez
  }
]
```

---

## 7. Szimulációs folyamat

Az alábbi szekvencia egy teljes futtatást ábrázol:

```
Program.cs
│
├─ CityGraph.LoadFromFile()
├─ LoadJson<Courier>()
├─ LoadJson<Order>()
│
└─ SimulationOrchestrator.RunAsync()
   │
   ├─ GreedyAssignmentService.AssignAll()     ← kezdeti kiosztás
   │
   ├─ ConcurrentQueue ← maradék Pending rendelések
   │
   └─ Task.WhenAll([CourierLoopAsync × n])    ← párhuzamos futás
      │
      └─ (minden futárra párhuzamosan)
         │
         ├─ NearestNeighborService.Optimize()
         │
         └─ DeliverySimulationService.SimulateAsync()
            │
            ├─ FindNearestWarehouse()
            ├─ TraversePath(futár → raktár)   ← UpdateTraffic() + Task.Delay()
            ├─ [csomag felvétel, 300ms]
            ├─ TraversePath(raktár → cím)     ← UpdateTraffic() + Task.Delay()
            └─ Késés-ellenőrzés + értesítés
```

---

## 8. Hibakezelés és megszakítás

### `CancellationToken`

A `Program.cs` egy `CancellationTokenSource`-t hoz létre, és a `Console.CancelKeyPress` eseményhez köti (`Ctrl+C`). A token az egész `RunAsync` → `CourierLoopAsync` → `SimulateAsync` → `TraversePath` láncon végighalad. A `TraversePath` `ct.ThrowIfCancellationRequested()`-et hív minden lépés előtt.

Ha a token megszakad:
- `OperationCanceledException` dob a `Task.Delay` vagy az explicit ellenőrzés
- Az orchestrátor fogja a kivételt, és `liveConsole.Finish()` után sárga üzenettel leáll

### JSON betöltési hibák

A `LoadJson<T>` segédfüggvény `JsonSerializer.Deserialize` hibát `Exception`-ként dobja, amely a szimuláció elindítása előtt leállítja a programot. Érvénytelen adatfájl esetén a hibaüzenet tartalmazza a fájl elérési útját.

---

## 9. Teljesítmény és skálázhatóság

| Paraméter | Jelenlegi | Megjegyzés |
|-----------|-----------|-----------|
| Csúcsok száma | 8 | Dijkstra `O(V²)` — 1000 csúcsig megfelelő |
| Futárok száma | 4 | Minden futár külön `Task` — TPL kezeli |
| Rendelések száma | 16 | `ConcurrentQueue` korlátlan méretű |
| `MsPerMinute` | 400ms | Csökkentéssel gyorsítható a szimuláció |

Nagy gráfok esetén (V > 1000) érdemes a Dijkstra-t prioritásos sorral (`O((V + E) log V)`) kiváltani.

---

## 10. Ismert korlátok

- **Nincs perzisztencia** — a szimuláció eredménye csak a konzolon jelenik meg, fájlba nem mentődik.
- **Nincs valódi DI konténer** — a függőségek kézzel vannak összekötve a `Program.cs`-ben.
- **Konzol-megjelenítő ablakméret-függő** — ha a terminál ablak túl kis, a megjelenítés csúszhat. Ajánlott minimális szélesség: 80 karakter.
- **Forgalom nem perzisztens** — minden `TraversePath` lépésnél frissül, de az újraindításkor visszaáll `1.0`-ra.
- **Zónán kívüli rendelések** — ha nincs megfelelő zónájú szabad futár, a rendelés `Pending` marad és nem kerül kézbesítésre.
