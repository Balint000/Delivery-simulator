namespace DeliverySimulator.Database.Entities;

public class SimulationRunEntity
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string CityName { get; set; } = "";
    public int TotalOrders { get; set; }
    public int CompletedOrders { get; set; }
    public ICollection<DeliveryLogEntity> Logs { get; set; } = [];
}
