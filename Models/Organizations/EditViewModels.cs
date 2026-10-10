using System.ComponentModel.DataAnnotations;

namespace SmartInventory.Models.Organizations;

public class OrganizationEditViewModel
{
    public int Id { get; set; }
    
    [Required(ErrorMessage = "Название организации обязательно")]
    public string Name { get; set; } = string.Empty;
    
    public string OwnerId { get; set; } = string.Empty;
    
    public List<BuildingEditViewModel> Buildings { get; set; } = [];
    public List<AccessCodeEditViewModel> AccessCodes { get; set; } = [];
}

public class BuildingEditViewModel
{
    public int Id { get; set; }
    [Required]
    public string Address { get; set; } = string.Empty;
    public string ManagerId { get; set; } = string.Empty;
}

public class AccessCodeEditViewModel
{
    public int Id { get; set; }
    [Required]
    public string Code { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsActivated { get; set; }
    public string? EmployeeName { get; set; }
}