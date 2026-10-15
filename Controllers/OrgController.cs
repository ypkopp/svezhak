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
        ViewBag.Ok = TempData["Ok"];
        ViewBag.Error = TempData["Error"];
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

    // POST /Org/Restaurants/Create — создать точку (только владелец)
    [HttpPost("Restaurants/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRestaurant(string address)
    {
        var orgId = await GetOrgIdAsync();
        if (orgId is null) return Forbid();
        if (string.IsNullOrWhiteSpace(address)) { TempData["Error"] = "Укажите адрес."; return RedirectToAction(nameof(Index)); }
        db.Restaurants.Add(new SmartInventory.Models.Structure.Restaurant
        {
            OrganizationId = orgId.Value,
            Address = address.Trim(),
            ManagerId = "pending",
        });
        await db.SaveChangesAsync();
        TempData["Ok"] = $"Точка «{address.Trim()}» добавлена.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Org/Employees — все сотрудники сети, секции по ресторанам
    [HttpGet("Employees")]
    public async Task<IActionResult> Employees()
    {
        var orgId = await GetOrgIdAsync();
        if (orgId is null) return Forbid();

        var restaurants = await db.Restaurants.AsNoTracking()
            .Where(r => r.OrganizationId == orgId).OrderBy(r => r.Id).ToListAsync();
        var codes = await db.AccessCodes.AsNoTracking()
            .Where(c => c.OrganizationId == orgId)
            .OrderBy(c => c.EmployeeName)
            .ToListAsync();

        var restIds = restaurants.Select(r => r.Id).ToList();
        // Точка сотрудника — ресторан его последней смены (у кода нет прямого RestaurantId)
        var shifts = await db.Shifts.AsNoTracking()
            .Where(s => restIds.Contains(s.RestaurantId))
            .Select(s => new { s.AccessCodeId, s.RestaurantId, s.Date })
            .ToListAsync();
        var restOf = shifts
            .GroupBy(s => s.AccessCodeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Date).First().RestaurantId);

        var byRestaurant = restIds.ToDictionary(id => id, _ => new List<SmartInventory.Models.Structure.AccessCode>());
        var unassigned = new List<SmartInventory.Models.Structure.AccessCode>();
        foreach (var c in codes)
        {
            // приоритет — явная привязка (RestaurantId), иначе последняя смена
            var rid = c.RestaurantId ?? (restOf.TryGetValue(c.Id, out var r0) ? r0 : (int?)null);
            if (rid.HasValue && byRestaurant.ContainsKey(rid.Value)) byRestaurant[rid.Value].Add(c);
            else unassigned.Add(c);
        }

        ViewBag.Restaurants = restaurants;
        ViewBag.ByRestaurant = byRestaurant;
        ViewBag.Unassigned = unassigned;
        ViewBag.SelfCodeId = int.TryParse(User.FindFirst("AccessCodeId")?.Value, out var sc) ? sc : -1;
        ViewBag.Ok = TempData["Ok"];
        ViewBag.Error = TempData["Error"];
        ViewBag.NewCode = TempData["NewCode"];
        ViewBag.NewCodeName = TempData["NewCodeName"];
        return View();
    }

    // POST /Org/Employees/Create — новый сотрудник любой роли (кроме владельца)
    [HttpPost("Employees/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEmployee(string employeeName, string role)
    {
        var orgId = await GetOrgIdAsync();
        if (orgId is null) return Forbid();
        if (string.IsNullOrWhiteSpace(employeeName)) { TempData["Error"] = "Укажите имя."; return RedirectToAction(nameof(Employees)); }

        var appRole = role switch
        {
            "админ" or nameof(AppRole.Admin) => AppRole.Admin,
            "менеджер" or nameof(AppRole.RestaurantManager) => AppRole.RestaurantManager,
            "шеф" or nameof(AppRole.HeadChef) => AppRole.HeadChef,
            _ => AppRole.Staff,
        };
        if (appRole == AppRole.OrganizationOwner) { TempData["Error"] = "Роль владельца не выдаётся."; return RedirectToAction(nameof(Employees)); }

        var code = new SmartInventory.Models.Structure.AccessCode(GenerateCode(), appRole, orgId.Value)
        {
            EmployeeName = employeeName.Trim(),
            IsActive = true,
        };
        db.AccessCodes.Add(code);
        await db.SaveChangesAsync();
        TempData["NewCode"] = code.Code;
        TempData["NewCodeName"] = code.EmployeeName;
        return RedirectToAction(nameof(Employees));
    }

    // POST /Org/Employees/Toggle — активировать/деактивировать код (не владельца)
    [HttpPost("Employees/Toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleEmployee(int id)
    {
        var orgId = await GetOrgIdAsync();
        if (orgId is null) return Forbid();
        var code = await db.AccessCodes.FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == orgId);
        if (code is null) return NotFound();
        if (code.Role == AppRole.OrganizationOwner)
        {
            TempData["Error"] = "Владельца сети нельзя деактивировать.";
            return RedirectToAction(nameof(Employees));
        }
        var selfId = int.TryParse(User.FindFirst("AccessCodeId")?.Value, out var sc) ? sc : -1;
        if (code.Id == selfId) { TempData["Error"] = "Нельзя деактивировать себя."; return RedirectToAction(nameof(Employees)); }

        code.IsActive = !code.IsActive;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Employees));
    }

    // POST /Org/Employees/Assign — привязать сотрудника к точке (или снять привязку)
    [HttpPost("Employees/Assign")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignEmployee(int id, int? restaurantId)
    {
        var orgId = await GetOrgIdAsync();
        if (orgId is null) return Forbid();
        var code = await db.AccessCodes.FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == orgId);
        if (code is null) return NotFound();
        if (code.Role == AppRole.OrganizationOwner) { TempData["Error"] = "Владельца нельзя привязать."; return RedirectToAction(nameof(Employees)); }
        if (restaurantId.HasValue)
        {
            var ok = await db.Restaurants.AnyAsync(r => r.Id == restaurantId.Value && r.OrganizationId == orgId);
            if (!ok) { TempData["Error"] = "Точка не найдена."; return RedirectToAction(nameof(Employees)); }
        }
        code.RestaurantId = restaurantId;
        await db.SaveChangesAsync();
        TempData["Ok"] = restaurantId.HasValue ? "Сотрудник привязан к точке." : "Привязка снята — сотрудник без точки.";
        return RedirectToAction(nameof(Employees));
    }

    // POST /Org/Employees/Delete — удалить access-код (не владельца и не себя)
    [HttpPost("Employees/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEmployee(int id)
    {
        var orgId = await GetOrgIdAsync();
        if (orgId is null) return Forbid();
        var code = await db.AccessCodes.FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == orgId);
        if (code is null) return NotFound();
        if (code.Role == AppRole.OrganizationOwner) { TempData["Error"] = "Владельца сети нельзя удалить."; return RedirectToAction(nameof(Employees)); }
        var selfId = int.TryParse(User.FindFirst("AccessCodeId")?.Value, out var sc) ? sc : -1;
        if (code.Id == selfId) { TempData["Error"] = "Нельзя удалить себя."; return RedirectToAction(nameof(Employees)); }

        // убрать смены на код, чтобы FK не мешал удалению
        var shifts = db.Shifts.Where(s => s.AccessCodeId == id);
        db.Shifts.RemoveRange(shifts);
        db.AccessCodes.Remove(code);
        await db.SaveChangesAsync();
        TempData["Ok"] = $"Код «{code.EmployeeName ?? code.Code}» удалён.";
        return RedirectToAction(nameof(Employees));
    }

    private static string GenerateCode()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var chars = new char[8];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        return new string(chars);
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
