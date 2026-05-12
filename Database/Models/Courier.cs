using System.ComponentModel.DataAnnotations.Schema;

namespace DeliverySimulator.Database.Models;

/// <summary>
/// Egy futár.
/// Adott zóna(k)ban dolgozik, max. MaxCapacity csomagot vihet egyszerre.
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
    public double AvgTime => DeliveriesCompleted > 0 ? (double)TotalTimeMinutes / DeliveriesCompleted : 0;
}
