namespace SmartInventory.Models.Structure;

public class Building
{
    public int Id { get; set; }
    public string Address { get; set; }
    
    public int OrganizationId { get; set; }
    public Organization Organization { get; set; }
    
    public string ManagerId { get; set; }
}