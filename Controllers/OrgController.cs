using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Authorization;
using SmartInventory.Data;
using SmartInventory.Services;

namespace SmartInventory.Controllers;

/// <summary>
/// Раздел владельца организации: карточки всех точек с KPI,
/// read-only просмотр дашборда ресторана, управление админами.
/// Доступ — только OrganizationOwner.
/// </summary>
[AuthorizeRole(AppRole.OrganizationOwner)]
[Route("Org")]
public sealed class OrgController(ApplicationDbContext db, OrderService orders) : Controller
{
    private async Task<int?> GetOrgIdAsync()
    {
        var s = User.FindFirstValue("AccessCodeId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(s, out var id)) return null;
        return await db.AccessCodes.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => (int?)c.OrganizationId)
            .FirstOrDefaultAsync();
    }

    // GET /Org — карточки ресторанов организации с KPI
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var orgId = await GetOrgIdAsync();
        if (orgId is null) return Forbid();
        var now = DateTime.UtcNow;
        var weekAgo = now.AddDays(-7);

        var restaurants = await db.Restaurants.AsNoTracking()
            .Where(r => r.OrganizationId == orgId)
            .OrderBy(r => r.Id)
            .ToListAsync();

        var cards = new List<object>();
        foreach (var r in restaurants)
        {
            var burning = await db.Batches
                .Where(b => b.RestaurantId == r.Id && b.QtyRemaining > 0 && b.ExpiresAt > now && b.ExpiresAt <= now.AddHours(24))
                .CountAsync();
            var stop = (await orders.GetStopListAsync(r.Id)).Count;
            var lostRub = await db.WriteOffs
                .Where(w => w.RestaurantId == r.Id && w.Reason == OrderService.ReasonExpired && w.CreatedAt >= weekAgo)
                .Join(db.Products, w => w.ProductId, p => p.Id, (w, p) => w.Qty * p.PurchasePrice)
                .SumAsync();
            cards.Add(new
            {
                r.Id,
                r.Address,
                Burning = burning,
                StopCount = stop,
                LostRub = Math.Round(lostRub, 0),
            });
        }

        ViewBag.Restaurants = cards;
        ViewBag.OrgName = await db.Organizations.Where(o => o.Id == orgId).Select(o => o.Name).FirstOrDefaultAsync();
        return View();
    }

    // GET /Org/View/{id} — read-only дашборд конкретного ресторана
    [HttpGet("View/{id}")]
    public async Task<IActionResult> View(int id)
    {
        var orgId = await GetOrgIdAsync();
        if (orgId is null) return Forbid();
        var restaurant = await db.Restaurants.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == orgId);
        if (restaurant is null) return NotFound();
        var now = DateTime.UtcNow;

        var batches = await db.Batches.AsNoTracking()
            .Include(b => b.Product)
            .Include(b => b.Delivery)
            .Where(b => b.RestaurantId == id && b.QtyRemaining > 0)
            .OrderBy(b => b.ExpiresAt)
            .ToListAsync();
        var live = batches.Where(b => b.ExpiresAt > now).ToList();
        var stop = await orders.GetStopListAsync(id);

        ViewBag.Restaurant = restaurant;
        ViewBag.Now = now;
        ViewBag.Live = live;
        ViewBag.StopList = stop;
        ViewBag.Expiring24 = live.Count(b => b.ExpiresAt <= now.AddHours(24));
        return View();
    }

    // ---------- Управление админами ----------

    // GET /Org/Admins — список кодов организации, выдать/отозвать Admin
    [HttpGet("Admins")]
    public async Task<IActionResult> Admins()
    {
        var orgId = await GetOrgIdAsync();
        if (orgId is null) return Forbid();
        var codes = await db.AccessCodes.AsNoTracking()
            .Where(c => c.OrganizationId == orgId)
            .OrderByDescending(c => c.Role == AppRole.Admin)
            .ThenBy(c => c.EmployeeName)
            .ToListAsync();
        ViewBag.Codes = codes;
        ViewBag.Ok = TempData["Ok"];
        ViewBag.Error = TempData["Error"];
        return View();
    }

    // POST /Org/Admins/Toggle — выдать/снять роль Admin
    [HttpPost("Admins/Toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAdmin(int id)
    {
        var orgId = await GetOrgIdAsync();
        if (orgId is null) return Forbid();

        var code = await db.AccessCodes.FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == orgId);
        if (code is null) return NotFound();
        if (code.Role == AppRole.OrganizationOwner)
        {
            TempData["Error"] = "Нельзя менять роль владельца.";
            return RedirectToAction(nameof(Admins));
        }

        code.Role = code.Role == AppRole.Admin ? AppRole.Staff : AppRole.Admin;
        await db.SaveChangesAsync();
        TempData["Ok"] = code.Role == AppRole.Admin
            ? $"{code.EmployeeName ?? code.Code} — теперь админ."
            : $"У {code.EmployeeName ?? code.Code} отозвана роль админа.";
        return RedirectToAction(nameof(Admins));
    }
}
