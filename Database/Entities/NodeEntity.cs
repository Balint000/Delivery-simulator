namespace DeliverySimulator.Database.Entities;

public class NodeEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Zone { get; set; } = "";
    public int CityId { get; set; }
    public CityEntity City { get; set; } = null!;
    // ASCII map koordináták (6. lépéshez, most null)
    public int? MapX { get; set; }
    public int? MapY { get; set; }
}
