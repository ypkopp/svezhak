using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Authorization;
using SmartInventory.Data;

namespace SmartInventory.Controllers;

[ApiController]
[Route("api/restaurants")]
public class RestaurantController(IAuthorizationService authorizationService, ApplicationDbContext db)
    : ControllerBase
{
    [HttpPost("{id}/settings")]
    public async Task<IActionResult> UpdateRestaurantSettings(int id, [FromBody] UpdateRestaurantDto dto)
    {
        var restaurant = await db.Restaurants
            .Include(b => b.Organization)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (restaurant == null)
            return NotFound();

        var authResult = await authorizationService.AuthorizeAsync(
            User,
            restaurant,
            nameof(AppPolicy.CanManageRestaurant));

        if (!authResult.Succeeded)
            return Forbid();

        restaurant.Address = dto.Address;

        await db.SaveChangesAsync();

        return Ok(new { Message = "Successfully updated restaurant" });
    }
}