using System.ComponentModel.DataAnnotations;

namespace SmartInventory.Models.Restaurants;

public class RestaurantEditViewModel
{
    public int Id { get; init; }

    [Required(ErrorMessage = "Адрес заведения обязателен")]
    public string Address { get; init; } = string.Empty;

    [Required(ErrorMessage = "ID управляющего обязателен")]
    public string ManagerId { get; init; } = string.Empty;
}