namespace SmartInventory.Models.Restaurants;

public class RestaurantIndexViewModel
{
    public int Id { get; init; }
    public string Address { get; init; } = string.Empty;
    
    public string ManagerId { get; init; } = string.Empty;
    public string OrganizationName { get; init; } = string.Empty;
}