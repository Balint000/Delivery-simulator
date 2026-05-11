using System.ComponentModel.DataAnnotations.Schema;

namespace DeliverySimulator.Database.Models;

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
