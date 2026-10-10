using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Authorization;
using SmartInventory.Data;

namespace SmartInventory.Controllers;

[ApiController]
[Route("api/buildings")]
public class BuildingsController(IAuthorizationService authorizationService, ApplicationDbContext db)
    : ControllerBase
{
    [HttpPost("{id}/settings")]
    public async Task<IActionResult> UpdateBuildingSettings(int id, [FromBody] UpdateBuildingDto dto)
    {
        var building = await db.Buildings
            .Include(b => b.Organization)
            .FirstOrDefaultAsync(b => b.Id == id);

        if (building == null)
            return NotFound();

        var authResult = await authorizationService.AuthorizeAsync(
            User,
            building,
            nameof(AppPolicy.CanManageBuilding));

        if (!authResult.Succeeded)
            return Forbid();

        building.Address = dto.Address;

        await db.SaveChangesAsync();

        return Ok(new { Message = "Successfully updated building" });
    }
}