using SmartInventory.Data;
using SmartInventory.Models.Organizations;
using SmartInventory.Models.Structure;

namespace SmartInventory.Extensions;

public static class OrganizationExtensions
{
    public static void UpdateRestaurants(this Organization organization, List<RestaurantEditViewModel> incomingRestaurants, ApplicationDbContext context)
    {
        var incomingIds = incomingRestaurants.Select(b => b.Id).ToList();
        
        var toRemove = organization.Restaurants
            .Where(b => !incomingIds.Contains(b.Id))
            .ToList();
        
        context.Restaurants.RemoveRange(toRemove);
        
        foreach (var bModel in incomingRestaurants)
        {
            if (bModel.Id == 0)
            {
                organization.Restaurants.Add(new Restaurant
                {
                    Address = bModel.Address,
                    ManagerId = bModel.ManagerId,
                    OrganizationId = organization.Id
                });
            }
            else
            {
                var restaurant = organization.Restaurants.FirstOrDefault(b => b.Id == bModel.Id);
                if (restaurant != null)
                {
                    restaurant.Address = bModel.Address;
                    restaurant.ManagerId = bModel.ManagerId;
                }
            }
        }
    }
    
    public static void UpdateAccessCodes(this Organization organization, List<AccessCodeEditViewModel> incomingCodes, ApplicationDbContext context)
    {
        var incomingIds = incomingCodes.Select(a => a.Id).ToList();
        
        var toRemove = organization.AccessCodes
            .Where(a => !incomingIds.Contains(a.Id))
            .ToList();
        
        context.AccessCodes.RemoveRange(toRemove);
        
        foreach (var cModel in incomingCodes)
        {
            if (cModel.Id == 0)
            {
                organization.AccessCodes.Add(new AccessCode(cModel.Code, cModel.Role, organization.Id)
                {
                    IsActive = cModel.IsActive,
                    IsActivated = cModel.IsActivated,
                    EmployeeName = cModel.EmployeeName
                });
            }
            else
            {
                var code = organization.AccessCodes.FirstOrDefault(a => a.Id == cModel.Id);
                if (code != null)
                {
                    code.Role = cModel.Role;
                    code.IsActive = cModel.IsActive;
                    code.IsActivated = cModel.IsActivated;
                    code.EmployeeName = cModel.EmployeeName;
                }
            }
        }
    }
}