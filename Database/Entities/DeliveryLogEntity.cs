namespace DeliverySimulator.Database.Entities;

public class DeliveryLogEntity
{
    public int Id { get; set; }
    public int SimulationRunId { get; set; }
    public SimulationRunEntity SimulationRun { get; set; } = null!;
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = "";
    public string EventType { get; set; } = ""; // "DELIVERY", "PICKUP", "TRAFFIC", stb.
}
