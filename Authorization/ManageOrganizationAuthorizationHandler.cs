using Microsoft.AspNetCore.Authorization;
using SmartInventory.Models.Structure;

namespace SmartInventory.Authorization;

public class ManageOrganizationRequirement : IAuthorizationRequirement;

public class ManageOrganizationAuthorizationHandler
    : AuthorizationHandler<ManageOrganizationRequirement, Organization>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ManageOrganizationRequirement requirement,
        Organization resource)
    {
        var user = context.User;

        if (user.IsInRole(nameof(AppRole.ServerAdmin)))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (user.IsInRole(nameof(AppRole.OrganizationOwner)))
        {
            var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (resource.OwnerId == userId)
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}