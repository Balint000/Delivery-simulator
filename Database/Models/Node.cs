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
