namespace DeliverySimulator.Database.Entities;

public class EdgeEntity
{
    public int Id { get; set; }
    public int FromNodeId { get; set; }
    public int ToNodeId { get; set; }
    public double Distance { get; set; }
    public int CityId { get; set; }
    public CityEntity City { get; set; } = null!;
}
