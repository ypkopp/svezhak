using Microsoft.AspNetCore.Authorization;

namespace SmartInventory.Authorization;

public class AuthorizeRoleAttribute : AuthorizeAttribute
{
    public AuthorizeRoleAttribute(params AppRole[] roles)
    {
        Roles = string.Join(",", roles.Select(r => r.ToString()));
    }
}