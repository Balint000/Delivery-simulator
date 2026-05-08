namespace DeliverySimulator.Database.Entities;

public class CourierEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double Speed { get; set; }
    public int Capacity { get; set; }
}
