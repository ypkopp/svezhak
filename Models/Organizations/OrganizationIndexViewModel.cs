namespace SmartInventory.Models.Organizations;

public class OrganizationIndexViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public int BuildingsCount { get; set; }
    public int AccessCodesCount { get; set; }
}