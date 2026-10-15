using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;

namespace SmartInventory.Controllers;

/// <summary>
/// /Schedule — расписание смены для поваров (read-only): своя неделя + команда.
/// Без форм редактирования — управление остаётся в /Admin/Schedule.
/// </summary>
[Authorize]
[Route("Schedule")]
public sealed class ScheduleController(ApplicationDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int weekOffset = 0)
    {
        var codeId = int.TryParse(
            User.FindFirstValue("AccessCodeId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var id) ? id : (int?)null;
        if (codeId is null) return Forbid();

        var me = await db.AccessCodes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == codeId);
        if (me is null) return Forbid();

        // Точка повара: явная привязка → последняя смена → первый ресторан сети
        int? rid = me.RestaurantId;
        if (rid is null)
        {
            rid = await db.Shifts.AsNoTracking()
                .Where(s => s.AccessCodeId == me.Id)
                .OrderByDescending(s => s.Date)
                .Select(s => (int?)s.RestaurantId)
                .FirstOrDefaultAsync();
        }
        rid ??= await db.Restaurants.AsNoTracking()
            .Where(r => r.OrganizationId == me.OrganizationId)
            .OrderBy(r => r.Id)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync();
        if (rid is null) return Forbid();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).AddDays(weekOffset * 7);
        var days = Enumerable.Range(0, 7).Select(monday.AddDays).ToList();

        var shifts = await db.Shifts.AsNoTracking()
            .Include(s => s.AccessCode)
            .Where(s => s.RestaurantId == rid && s.Date >= days[0] && s.Date <= days[6])
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        var restaurant = await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid);
        ViewBag.Days = days;
        ViewBag.WeekOffset = weekOffset;
        ViewBag.Today = today;
        ViewBag.MeId = me.Id;
        ViewBag.RestaurantName = restaurant?.Address ?? "";
        return View(shifts);
    }
}
