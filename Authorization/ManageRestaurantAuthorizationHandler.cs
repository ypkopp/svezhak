using SmartInventory.Models.Structure;
using Microsoft.AspNetCore.Authorization;

namespace SmartInventory.Authorization;

public class ManageRestaurantRequirement : IAuthorizationRequirement
{
}

public class ManageRestaurantAuthorizationHandler
    : AuthorizationHandler<ManageRestaurantRequirement, Restaurant>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ManageRestaurantRequirement requirement,
        Restaurant resource)
    {
        var user = context.User;

        if (user.IsInRole(nameof(AppRole.ServerAdmin)))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var isOwner = user.IsInRole(nameof(AppRole.OrganizationOwner));
        if (isOwner && resource.Organization.OwnerId == userId)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var isManager = user.IsInRole(nameof(AppRole.RestaurantManager));
        if (isManager && resource.ManagerId == userId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}