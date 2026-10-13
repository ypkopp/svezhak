using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Services;

namespace SmartInventory.Controllers;

[Authorize]
[Route("Inventory/Advisor")]
public sealed class AdvisorController(ApplicationDbContext db, AdvisorService advisor) : Controller
{
    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var restaurantId = await GetRestaurantIdAsync(ct);
        if (restaurantId is null) return Forbid();
        var result = await advisor.GetInsightsAsync(restaurantId.Value, ct);
        return View("~/Views/Inventory/Advisor.cshtml", result);
    }

    [HttpGet("Insights")]
    public async Task<IActionResult> Insights(CancellationToken ct)
    {
        var restaurantId = await GetRestaurantIdAsync(ct);
        if (restaurantId is null) return Forbid();
        return Json(await advisor.GetInsightsAsync(restaurantId.Value, ct));
    }

    private async Task<int?> GetRestaurantIdAsync(CancellationToken ct)
    {
        var codeValue = User.FindFirstValue("AccessCodeId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(codeValue, out var accessCodeId)) return null;
        var organizationId = await db.AccessCodes.AsNoTracking()
            .Where(x => x.Id == accessCodeId)
            .Select(x => (int?)x.OrganizationId)
            .FirstOrDefaultAsync(ct);
        if (organizationId is null) return null;
        return await db.Restaurants.AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .Select(x => (int?)x.Id)
            .FirstOrDefaultAsync(ct);
    }
}
