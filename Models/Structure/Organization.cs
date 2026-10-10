using SmartInventory.Models.Account;

namespace SmartInventory.Models.Structure;

public class Organization
{
    public int Id { get; set; }
    public string Name { get; set; }
    public List<Building> Buildings { get; set; } = [];
    public List<AccessCode> AccessCodes { get; set; } = [];
    public string OwnerId { get; set; }
}