using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Authorization;
using SmartInventory.Data;
using SmartInventory.Models.Inventory;
using SmartInventory.Models.Structure;

namespace SmartInventory.Controllers;

/// <summary>
/// Раздел «Админ»: управление сотрудниками (access-коды ресторана)
/// и недельное расписание смен. Доступен только роли Admin.
/// </summary>
[AuthorizeRole(AppRole.Admin)]
[Route("Admin")]
public sealed class AdminController(ApplicationDbContext db) : Controller
{
    private static readonly string[] AllowedPositions = ["шеф", "повар", "менеджер"];

    // ---------- helpers ----------

    private int? CurrentAccessCodeId()
    {
        var s = User.FindFirstValue("AccessCodeId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(s, out var id) ? id : null;
    }

    private async Task<int?> GetOrganizationIdAsync()
    {
        var codeId = CurrentAccessCodeId();
        if (codeId is null) return null;
        return await db.AccessCodes.AsNoTracking()
            .Where(c => c.Id == codeId)
            .Select(c => (int?)c.OrganizationId)
            .FirstOrDefaultAsync();
    }

    private async Task<int?> GetRestaurantIdAsync()
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId is null) return null;
        return await db.Restaurants.AsNoTracking()
            .Where(r => r.OrganizationId == orgId)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync();
    }

    // ---------- Сотрудники ----------

    // GET /Admin/Employees
    [HttpGet("Employees")]
    public async Task<IActionResult> Employees()
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId is null) return Forbid();

        var codes = await db.AccessCodes.AsNoTracking()
            .Where(c => c.OrganizationId == orgId)
            .OrderByDescending(c => c.IsActive)
            .ThenBy(c => c.EmployeeName)
            .ToListAsync();

        ViewBag.Codes = codes;
        ViewBag.SelfCodeId = CurrentAccessCodeId();
        ViewBag.NewCode = TempData["NewCode"];
        ViewBag.NewCodeName = TempData["NewCodeName"];
        ViewBag.Error = TempData["Error"];
        ViewBag.Roles = new[] { (AppRole.Staff, "повар"), (AppRole.Manager, "менеджер"), (AppRole.Admin, "админ") };
        return View();
    }

    // POST /Admin/Employees/Create — сгенерировать код для нового сотрудника
    [HttpPost("Employees/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEmployee(string employeeName, string role)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId is null) return Forbid();

        if (string.IsNullOrWhiteSpace(employeeName))
        {
            TempData["Error"] = "Укажите имя сотрудника.";
            return RedirectToAction(nameof(Employees));
        }

        // Роль — только из белого списка, чтобы формой нельзя было выдать лишнее
        var appRole = role switch
        {
            "админ" or nameof(AppRole.Admin) => AppRole.Admin,
            "менеджер" or nameof(AppRole.Manager) => AppRole.Manager,
            _ => AppRole.Staff,
        };

        var code = GenerateCode();
        var accessCode = new AccessCode(code, appRole, orgId.Value)
        {
            EmployeeName = employeeName.Trim(),
            IsActive = true,
        };
        db.AccessCodes.Add(accessCode);
        await db.SaveChangesAsync();

        // Код показывается ровно один раз — через TempData на следующем рендере
        TempData["NewCode"] = code;
        TempData["NewCodeName"] = accessCode.EmployeeName;
        return RedirectToAction(nameof(Employees));
    }

    // POST /Admin/Employees/Toggle — активировать / деактивировать
    [HttpPost("Employees/Toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleEmployee(int id)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId is null) return Forbid();

        var code = await db.AccessCodes.FirstOrDefaultAsync(c => c.Id == id && c.OrganizationId == orgId);
        if (code is null) return NotFound();

        if (code.Id == CurrentAccessCodeId())
        {
            TempData["Error"] = "Нельзя деактивировать самого себя.";
            return RedirectToAction(nameof(Employees));
        }

        code.IsActive = !code.IsActive;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Employees));
    }

    // ---------- Рестораны ----------

    // GET /Admin/Restaurants
    [HttpGet("Restaurants")]
    public async Task<IActionResult> Restaurants(string? sort = null)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId is null) return Forbid();

        var list = await db.Restaurants.AsNoTracking()
            .Where(r => r.OrganizationId == orgId)
            .ToListAsync();

        list = sort switch
        {
            "address" => list.OrderBy(r => r.Address).ToList(),
            _ => list.OrderBy(r => r.Id).ToList(),
        };

        ViewBag.Restaurants = list;
        ViewBag.Sort = sort;
        ViewBag.Error = TempData["Error"];
        ViewBag.Ok = TempData["Ok"];
        return View();
    }

    // POST /Admin/Restaurants/Create — добавить ресторан
    [HttpPost("Restaurants/Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRestaurant(string address)
    {
        var orgId = await GetOrganizationIdAsync();
        if (orgId is null) return Forbid();

        if (string.IsNullOrWhiteSpace(address))
        {
            TempData["Error"] = "Укажите адрес ресторана.";
            return RedirectToAction(nameof(Restaurants));
        }

        db.Restaurants.Add(new Restaurant
        {
            OrganizationId = orgId.Value,
            Address = address.Trim(),
            ManagerId = "pending",
        });
        await db.SaveChangesAsync();
        TempData["Ok"] = $"Ресторан «{address.Trim()}» добавлен.";
        return RedirectToAction(nameof(Restaurants));
    }

    // ---------- Ассортимент (импорт продуктов) ----------

    // GET /Admin/Products — список + загрузка файла
    [HttpGet("Products")]
    public async Task<IActionResult> Products()
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();

        var products = await db.Products.AsNoTracking()
            .Where(p => p.RestaurantId == rid)
            .OrderBy(p => p.Category).ThenBy(p => p.Name)
            .ToListAsync();

        ViewBag.Products = products;
        ViewBag.ImportRows = TempData["ImportRows"] is string json && !string.IsNullOrEmpty(json)
            ? SmartInventory.Services.ProductImportService.DeserializeRows(json) : null;
        if (ViewBag.ImportRows is not null)
            SmartInventory.Services.ProductImportService.Validate((List<SmartInventory.Services.ProductImportService.ImportRow>)ViewBag.ImportRows, products.Select(p => p.Name));
        ViewBag.Error = TempData["Error"];
        ViewBag.Ok = TempData["Ok"];
        return View();
    }

    // POST /Admin/Products/Import — файл → предпросмотр (строки в TempData → GET)
    [HttpPost("Products/Import")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportProducts(IFormFile? file)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        if (file is null || file.Length == 0) { TempData["Error"] = "Файл не выбран."; return RedirectToAction(nameof(Products)); }

        List<SmartInventory.Services.ProductImportService.ImportRow> rows;
        await using (var s = file.OpenReadStream())
            rows = SmartInventory.Services.ProductImportService.Parse(s, file.FileName);
        if (rows.Count == 0) { TempData["Error"] = "В файле нет строк."; return RedirectToAction(nameof(Products)); }

        TempData["ImportRows"] = SmartInventory.Services.ProductImportService.SerializeRows(rows);
        return RedirectToAction(nameof(Products));
    }

    // POST /Admin/Products/Confirm — подтверждение предпросмотра
    [HttpPost("Products/Confirm")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmProducts(string importJson)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();

        var rows = SmartInventory.Services.ProductImportService.DeserializeRows(importJson);
        var existing = await db.Products.AsNoTracking().Where(p => p.RestaurantId == rid).Select(p => p.Name).ToListAsync();
        SmartInventory.Services.ProductImportService.Validate(rows, existing);
        var valid = rows.Where(r => r.Errors.Count == 0).ToList();
        if (valid.Count == 0) { TempData["Error"] = "Нет валидных строк."; return RedirectToAction(nameof(Products)); }

        foreach (var r in valid)
            db.Products.Add(new Product { RestaurantId = rid.Value, Name = r.Name.Trim(), Category = string.IsNullOrWhiteSpace(r.Category) ? "Прочее" : r.Category.Trim(), Unit = r.Unit.Trim(), PurchasePrice = r.PurchasePrice });
        await db.SaveChangesAsync();
        TempData["Ok"] = $"Импортировано {valid.Count} продуктов.";
        return RedirectToAction(nameof(Products));
    }

    private static string GenerateCode()
    {
        // 8 символов: читаемый набор без неоднозначных (0/O, 1/I)
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        var chars = new char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        return new string(chars);
    }

    // ---------- Расписание ----------

    // GET /Admin/Schedule?weekOffset=-1|0|1
    [HttpGet("Schedule")]
    public async Task<IActionResult> Schedule(int weekOffset = 0)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Понедельник текущей недели + сдвиг
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).AddDays(weekOffset * 7);
        var days = Enumerable.Range(0, 7).Select(monday.AddDays).ToList();

        var shifts = await db.Shifts.AsNoTracking()
            .Include(s => s.AccessCode)
            .Where(s => s.RestaurantId == rid && s.Date >= days[0] && s.Date <= days[6])
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        var orgId = await GetOrganizationIdAsync();
        var employees = await db.AccessCodes.AsNoTracking()
            .Where(c => c.OrganizationId == orgId && c.IsActive)
            .OrderBy(c => c.EmployeeName)
            .ToListAsync();

        ViewBag.Days = days;
        ViewBag.WeekOffset = weekOffset;
        ViewBag.Today = today;
        ViewBag.Shifts = shifts.GroupBy(s => s.Date).ToDictionary(g => g.Key, g => g.ToList());
        ViewBag.Employees = employees;
        ViewBag.Positions = AllowedPositions;
        ViewBag.Error = TempData["Error"];
        return View();
    }

    // POST /Admin/Schedule/Add — добавить смену
    [HttpPost("Schedule/Add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddShift(int accessCodeId, DateOnly date, TimeOnly startTime, TimeOnly endTime, string position, int weekOffset = 0)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();

        if (endTime <= startTime)
        {
            TempData["Error"] = "Конец смены должен быть позже начала.";
            return RedirectToAction(nameof(Schedule), new { weekOffset });
        }
        if (!AllowedPositions.Contains(position))
        {
            TempData["Error"] = "Недопустимая должность.";
            return RedirectToAction(nameof(Schedule), new { weekOffset });
        }

        // Сотрудник должен принадлежать той же организации, что и ресторан
        var orgId = await GetOrganizationIdAsync();
        var employee = await db.AccessCodes
            .FirstOrDefaultAsync(c => c.Id == accessCodeId && c.OrganizationId == orgId && c.IsActive);
        if (employee is null)
        {
            TempData["Error"] = "Сотрудник не найден или неактивен.";
            return RedirectToAction(nameof(Schedule), new { weekOffset });
        }

        // Без пересечений у одного сотрудника в один день
        var overlaps = await db.Shifts.AnyAsync(s =>
            s.RestaurantId == rid && s.AccessCodeId == accessCodeId && s.Date == date
            && s.StartTime < endTime && startTime < s.EndTime);
        if (overlaps)
        {
            TempData["Error"] = $"У {employee.EmployeeName} уже есть смена, пересекающаяся по времени.";
            return RedirectToAction(nameof(Schedule), new { weekOffset });
        }

        db.Shifts.Add(new Shift
        {
            RestaurantId = rid.Value,
            AccessCodeId = accessCodeId,
            Date = date,
            StartTime = startTime,
            EndTime = endTime,
            Position = position,
        });
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Schedule), new { weekOffset });
    }

    // POST /Admin/Schedule/Delete — удалить смену
    [HttpPost("Schedule/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteShift(int id, int weekOffset = 0)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();

        var shift = await db.Shifts.FirstOrDefaultAsync(s => s.Id == id && s.RestaurantId == rid);
        if (shift is null) return NotFound();
        db.Shifts.Remove(shift);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Schedule), new { weekOffset });
    }
}
