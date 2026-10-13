using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Services;

namespace SmartInventory.Components;

/// <summary>
/// Виджет «Смена» в сайдбаре. Показывает активную смену ресторана,
/// либо — для админа при полном отсутствии смен — кнопку настройки.
/// </summary>
public sealed class ShiftWidgetViewComponent(ApplicationDbContext db, ShiftService shifts) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var principal = (ClaimsPrincipal)User;
        var isAdmin = principal.IsInRole("Admin");

        var restaurantId = await GetRestaurantIdAsync(principal);
        var model = new ShiftWidgetModel(isAdmin, null, false);

        if (restaurantId is not null)
        {
            var current = await shifts.GetCurrentAsync(restaurantId.Value, HttpContext.RequestAborted);
            var hasAny = current is not null || await shifts.HasAnyAsync(restaurantId.Value, HttpContext.RequestAborted);
            model = model with { Current = current, HasAnyShifts = hasAny };
        }

        return View(model);
    }

    private async Task<int?> GetRestaurantIdAsync(ClaimsPrincipal user)
    {
        var codeStr = user.FindFirstValue("AccessCodeId") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(codeStr, out var codeId)) return null;
        var orgId = await db.AccessCodes.AsNoTracking()
            .Where(c => c.Id == codeId)
            .Select(c => (int?)c.OrganizationId)
            .FirstOrDefaultAsync();
        if (orgId is null) return null;
        return await db.Restaurants.AsNoTracking()
            .Where(r => r.OrganizationId == orgId)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync();
    }
}

public sealed record ShiftWidgetModel(bool IsAdmin, CurrentShift? Current, bool HasAnyShifts);
