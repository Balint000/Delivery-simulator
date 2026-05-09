using System.ComponentModel.DataAnnotations.Schema;

namespace DeliverySimulator.Database.Models;

/// <summary>
/// Egy teljes szimuláció futásának összesített eredménye.
/// Minden futás után mentésre kerül a DB-be.
/// </summary>
public class SimulationRun
{
    public int Id { get; set; }
    public int CityId { get; set; }
    public DateTime RunAt { get; set; }
    public int Total { get; set; }
    public int Delivered { get; set; }
    public int Late { get; set; }
    public int Unassigned { get; set; }
    public double ElapsedSeconds { get; set; }

    // Navigációs property — EF tölti fel
    public List<DeliveryLog> Logs { get; set; } = [];
}
