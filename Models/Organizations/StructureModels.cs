using System.ComponentModel.DataAnnotations;
using SmartInventory.Authorization;

namespace SmartInventory.Models.Structure;

public class Organization
{
    public int Id { get; init; }
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    public List<Restaurant> Restaurants { get; init; } = [];
    public List<AccessCode> AccessCodes { get; init; } = [];

    [MaxLength(100)]
    public string OwnerId { get; set; } = null!;
}

public class Restaurant
{
    public int Id { get; init; }
    [MaxLength(100)]
    public string Address { get; set; } = null!;

    public int OrganizationId { get; init; }
    public Organization Organization { get; init; } = null!;

    [MaxLength(100)]
    public string ManagerId { get; set; } = null!;

    // Публичный токен терминала кухни — read-only стоп-лист по /Kitchen/{token}
    public Guid KitchenToken { get; set; } = Guid.NewGuid();
}

public class AccessCode
{
    public int Id { get; init; }

    [MaxLength(100)]
    public string Code { get; init; } = null!;

    public AppRole Role { get; set; } = AppRole.Staff;
    public bool IsActive { get; set; } = true;
    public bool IsActivated { get; set; }
    [MaxLength(100)]
    public string? EmployeeName { get; set; }

    public int OrganizationId { get; init; }
    public Organization Organization { get; init; } = null!;

    // Точка привязки сотрудника (owner назначает). Если null — берём из последней смены.
    public int? RestaurantId { get; set; }

    public AccessCode() { }

    public AccessCode(string code, AppRole role, int organizationId)
    {
        Code = code;
        Role = role;
        OrganizationId = organizationId;
    }
}