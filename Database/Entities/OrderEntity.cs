namespace DeliverySimulator.Database.Entities;

public class OrderEntity
{
    public int Id { get; set; }
    public string Customer { get; set; } = "";
    public int DestinationNodeId { get; set; }
    public string Zone { get; set; } = "";
    public int Priority { get; set; }
}
