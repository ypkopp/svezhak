namespace SmartInventory.Models.Organizations;

public class OrganizationDetailsViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;

    public List<BuildingViewModel> Buildings { get; set; } = [];
    public List<AccessCodeViewModel> AccessCodes { get; set; } = [];
}

public class BuildingViewModel
{
    public int Id { get; set; }
    public string Address { get; set; } = string.Empty;
    public string ManagerId { get; set; } = string.Empty;
}

public class AccessCodeViewModel
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsActivated { get; set; }
    public string? EmployeeName { get; set; }
}