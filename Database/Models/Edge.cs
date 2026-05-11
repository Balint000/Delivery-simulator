using System.ComponentModel.DataAnnotations.Schema;

namespace DeliverySimulator.Database.Models;

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
