using System.ComponentModel.DataAnnotations.Schema;

namespace DeliverySimulator.Database.Models;

/// <summary>
/// Egy csúcs a városgráfban (raktár, kézbesítési pont, kereszteződés).
/// </summary>
public class Node
{
    public int Id { get; set; }
    public int CityId { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";   // "Warehouse" | "Delivery" | "Junction"
    public int? ZoneId { get; set; }
}

/// <summary>
/// Egy él a városgráfban — két csúcs közötti út menetideje.
/// A forgalom (TrafficMultiplier) módosítja az aktuális időt.
/// </summary>
public class Edge
{
    public int Id { get; set; }
    public int CityId { get; set; }
    public int From { get; set; }
    public int To { get; set; }
    public int IdealMinutes { get; set; }   // forgalom nélküli alap
    [NotMapped] public double TrafficMultiplier { get; set; } = 1.0;
    // Az aktuális (forgalommal számolt) menetidő
    [NotMapped] public int CurrentMinutes => (int)(IdealMinutes * TrafficMultiplier);
}

/// <summary>
/// Egy futár. Zónájában dolgozik, max. MaxCapacity csomagot vihet egyszerre.
/// </summary>
public class Courier
{
    public int Id { get; set; }
    public int CityId { get; set; }
    public string Name { get; set; } = "";
    public int CurrentNodeId { get; set; }   // gráf-csúcs ahol éppen van
    public List<int> ZoneIds { get; set; } = [];
    public int MaxCapacity { get; set; } = 3;
    [NotMapped] public List<int> AssignedOrderIds { get; set; } = [];

    // Statisztikák — a szimuláció tölti fel
    [NotMapped] public int DeliveriesCompleted { get; set; } = 0;
    [NotMapped] public int LateDeliveries { get; set; } = 0;
    [NotMapped] public int TotalTimeMinutes { get; set; } = 0;

    // Segédtulajdonságok
    public bool HasRoom => AssignedOrderIds.Count < MaxCapacity;
    public int FreeSlots => MaxCapacity - AssignedOrderIds.Count;
    public bool CanServe(int zoneId) => ZoneIds.Contains(zoneId);
    public double AvgTime => DeliveriesCompleted > 0
                                    ? (double)TotalTimeMinutes / DeliveriesCompleted : 0;
}

/// <summary>
/// Egy kézbesítési megrendelés.
/// </summary>
public class Order
{
    public int Id { get; set; }
    public int CityId { get; set; }
    public string Number { get; set; } = "";
    public string Customer { get; set; } = "";
    public string Address { get; set; } = "";
    public int AddressNodeId { get; set; }
    public int ZoneId { get; set; }

    // Státusz
    [NotMapped] public OrderStatus Status { get; set; } = OrderStatus.Pending;
    [NotMapped] public int? AssignedCourierId { get; set; }

    // Időmérés — a szimuláció tölti fel
    [NotMapped] public int? IdealMinutes { get; set; }
    [NotMapped] public int? ActualMinutes { get; set; }
    [NotMapped] public bool WasLate { get; set; } = false;

    // Késés mértéke percben
    public int LateMinutes => WasLate && IdealMinutes.HasValue && ActualMinutes.HasValue
                                  ? ActualMinutes.Value - IdealMinutes.Value : 0;
}

/// <summary>
/// Rendelés lehetséges állapotai.
/// </summary>
public enum OrderStatus
{
    Pending,    // Várakozik
    Assigned,   // Futárhoz rendelve
    InTransit,  // Úton
    Delivered,  // Kézbesítve
}
