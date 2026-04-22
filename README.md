# DeliverySimulator

A **.NET 10** konzolos alkalmazás, amely egy városi csomagkézbesítési rendszert szimulál. A program valós idejű, párhuzamos futárszimuláción, gráfalapú navigáción és mohó hozzárendelési algoritmuson alapul.

---

## Tartalomjegyzék

- [Funkciók](#funkciók)
- [Előfeltételek](#előfeltételek)
- [Telepítés és futtatás](#telepítés-és-futtatás)
- [Projekt struktúra](#projekt-struktúra)
- [Konfiguráció](#konfiguráció)
- [Algoritmusok](#algoritmusok)
- [Kimenetek és riportok](#kimenetek-és-riportok)
- [Csapat](#csapat)

---

## Funkciók

- **Városgráf** — Dijkstra-algoritmus alapú legrövidebb útvonal keresés
- **Dinamikus forgalomszimuláció** — véletlenszerű forgalomváltozás minden élen, balesetszimulációval
- **Greedy futár-hozzárendelés** — a legközelebbi szabad futár kap minden rendelést
- **Nearest Neighbor útvonal-optimalizálás** — közelítő TSP-megoldás több csomag esetén
- **Párhuzamos futárszimulációk** — `Task.WhenAll` + `ConcurrentQueue` a szálbiztos adatkezelésért
- **Valós idejű konzol UI** — a futárok státusza és az eseménynapló élőben frissül
- **Késés-detektálás és értesítés** — 10%-os tolerancia felett az ügyfél értesítést kap
- **Végső riportok** — késési riport, futár-teljesítmény rangsor, zónánkénti terhelés

---

## Előfeltételek

| Eszköz | Minimális verzió |
|--------|-----------------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0 |

Ellenőrzés:

```bash
dotnet --version
```

---

## Telepítés és futtatás

```bash
# Klónozás
git clone https://github.com/Balint000/Delivery-simulator.git
cd DeliverySimulator

# Futtatás
dotnet run

# vagy Release módban
dotnet run --configuration Release
```

> A program az adatfájlokat a `Data/` könyvtárból tölti be automatikusan.

---

## Projekt struktúra

```
DeliverySimulator/
├── Data/
│   ├── city.json          # Városgráf (csúcsok, élek, menetidők)
│   ├── couriers.json      # Futárok adatai
│   └── orders.json        # Rendelések listája
│
├── Display/
│   ├── LiveConsole.cs     # Valós idejű konzol megjelenítő
│   └── Reports.cs         # Végső statisztikai riportok
│
├── Graph/
│   └── CityGraph.cs       # Gráf adatstruktúra + Dijkstra + forgalomszimuláció
│
├── Models/
│   └── Models.cs          # Adatmodellek (Node, Edge, Courier, Order)
│
├── Services/
│   ├── DeliverySimulationService.cs   # Egy futár egy körének szimulációja
│   ├── GreedyAssignmentService.cs     # Mohó hozzárendelési algoritmus
│   ├── NearestNeighborService.cs      # NN útvonal-optimalizálás
│   └── SimulationOrchestrator.cs      # Teljes szimuláció vezénylése (TPL)
│
├── Program.cs             # Belépési pont: setup, wiring, szimuláció, riportok
└── DeliverySimulator.csproj
```

---

## Konfiguráció

Az adatfájlok (`Data/*.json`) szerkesztésével a szimuláció paraméterei szabadon módosíthatók. Nincs szükség újrafordításra.

### `Data/city.json`

A városgráf definíciója. Csúcstípusok: `Warehouse`, `Delivery`, `Junction`.

```json
{
  "cityName": "Demo Város",
  "nodes": [
    { "id": 0, "name": "Raktár (Zóna 1)", "type": "Warehouse", "zoneId": 1 }
  ],
  "edges": [
    { "from": 0, "to": 1, "idealTimeMinutes": 5 }
  ]
}
```

### `Data/couriers.json`

Futárok listája. A `zoneIds` mező határozza meg, melyik zónában dolgozhat az adott futár.

```json
[
  { "id": 1, "name": "Kovács János", "currentNodeId": 0, "zoneIds": [1], "maxCapacity": 3 }
]
```

### `Data/orders.json`

Kézbesítési rendelések. Az `addressNodeId` a célcsúcsra, a `zoneId` a zóna szűrésre vonatkozik.

```json
[
  { "id": 1, "number": "ORD-001", "customer": "Molnár Rita", "address": "Váci út 10", "addressNodeId": 1, "zoneId": 1 }
]
```

### Szimulációs konstansok (kódban)

| Konstans | Helye | Alapértelmezett | Leírás |
|---|---|---|---|
| `MsPerMinute` | `DeliverySimulationService.cs` | `400` | 1 szimulált perc = X ms valós időben |
| `DelayThreshold` | `DeliverySimulationService.cs` | `1.10` | Késési küszöb (10% tolerancia) |

---

## Algoritmusok

### Dijkstra (legrövidebb út)

A `CityGraph.FindShortestPath(from, to)` metódus az aktuális forgalommal számolt élsúlyok alapján keresi a legrövidebb utat. Az `IdealTime(from, to)` forgalom nélkül futja ugyanezt — ez az összehasonlítás alapja a késés-detektáláshoz.

### Greedy hozzárendelés

Minden `Pending` rendeléshez megkeresi a legközelebbi (Dijkstra szerint) szabad, megfelelő zónájú futárt. Nem garantál globális optimumot, de futási ideje lineáris a rendelések × futárok számában.

### Nearest Neighbor (TSP-közelítés)

Ha egy futárnak több csomagja van, a `NearestNeighborService.Optimize()` sorrendbe rendezi őket: mindig a jelenlegi pozícióhoz legközelebbi következő cél kerül sorra. Ez megközelítőleg optimális, de NP-teljes pontos megoldás nélkül.

### Párhuzamos szimuláció (TPL)

Az orchestrátor `Task.WhenAll`-lal indítja el az összes futár munkaciklusát egyszerre. A maradék rendelések `ConcurrentQueue<Order>`-ben várnak; a `TryDequeue()` atomikus hívás garantálja, hogy egy rendelést csak egy futár kap meg.

---

## Kimenetek és riportok

A szimuláció végén három riport jelenik meg a konzolon:

**1. Késési riport** — késett rendelések listája, késés szerint csökkentő sorrendben, futárral és percekkel.

**2. Futár teljesítmény rangsor** — rendezés: legtöbb kézbesítés → legkevesebb késés → leggyorsabb átlagidő.

**3. Zónánkénti terhelés** — összesített és hatékonysági statisztikák zónánként, az aktív futárok nevével.

---

## Csapat

@Balint000
@Mogyi13
