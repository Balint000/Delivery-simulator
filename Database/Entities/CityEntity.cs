namespace DeliverySimulator.Database.Entities;

public class CityEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ICollection<NodeEntity> Nodes { get; set; } = [];
    public ICollection<EdgeEntity> Edges { get; set; } = [];
}
