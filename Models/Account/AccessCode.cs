using SmartInventory.Authorization;
using SmartInventory.Models.Structure;

namespace SmartInventory.Models.Account;

public class AccessCode
{
    public int Id { get; set; }
    
    public string Code { get; private set; } = string.Empty; 
    
    public string RoleName { get; set; } = nameof(AppRole.Staff);
    public bool IsActive { get; set; } = true; 
    public bool IsActivated { get; set; } = false; 
    public string? EmployeeName { get; set; } 
    
    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public AccessCode() { }

    public AccessCode(string code, string roleName, int organizationId)
    {
        Code = code;
        RoleName = roleName;
        OrganizationId = organizationId;
    }
}