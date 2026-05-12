using System.ComponentModel.DataAnnotations.Schema;

namespace DeliverySimulator.Database.Models;

/// <summary>
/// Egy rendelés kézbesítésének részletes naplója.
/// Minden futáshoz tartozik annyi DeliveryLog, ahány rendelés volt.
/// </summary>
public class DeliveryLog
{
    public int Id { get; set; }
    public int SimRunId { get; set; }
    public string OrderNumber { get; set; } = "";
    public string Customer { get; set; } = "";
    public int? CourierId { get; set; }
    public string? CourierName { get; set; }
    public bool WasDelivered { get; set; }
    public bool WasLate { get; set; }
    public int? IdealMinutes { get; set; }
    public int? ActualMinutes { get; set; }
}
