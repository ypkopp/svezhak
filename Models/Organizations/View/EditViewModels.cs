using System.ComponentModel.DataAnnotations;
using SmartInventory.Authorization;
using SmartInventory.Models.Restaurants;

namespace SmartInventory.Models.Organizations;

public class OrganizationEditViewModel
{
    public int Id { get; init; }
    
    [Required(ErrorMessage = "Название организации обязательно")]
    public string Name { get; init; } = string.Empty;
    
    public List<RestaurantEditViewModel> Restaurants { get; init; } = [];
    public List<AccessCodeEditViewModel> AccessCodes { get; init; } = [];
    
    [Required(ErrorMessage = "ID владельца обязателен")]
    public string OwnerId { get; init; } = string.Empty;
}

public class AccessCodeEditViewModel
{
    public int Id { get; init; }
    [Required(ErrorMessage = "Введите код!")]
    public string Code { get; init; } = string.Empty;
    [Required(ErrorMessage = "Роли обязательна")]
    public AppRole Role { get; init; } = AppRole.Staff;
    public bool IsActive { get; init; }
    public bool IsActivated { get; init; }
    public string? EmployeeName { get; init; }
}