namespace SmartInventory.Models.Organizations;

public class OrganizationIndexViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string OwnerId { get; init; } = string.Empty;
    public int RestaurantsCount { get; init; }
    public int AccessCodesCount { get; init; }
}