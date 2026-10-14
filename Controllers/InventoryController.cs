using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Authorization;
using SmartInventory.Data;
using SmartInventory.Models.Inventory;
using SmartInventory.Services;

namespace SmartInventory.Controllers;

/// <summary>
/// Интерфейс «СВЕЖАК»: дашборд, склад, поставки, заказ, стоп-лист.
/// Всё scoped по ресторану текущего пользователя.
/// </summary>
[Authorize]
public class InventoryController(
    ApplicationDbContext db,
    OrderService orders,
    HotOffersService hotOffersService,
    AdvisorService advisorService,
    IServiceProvider services,
    InventoryEventBus eventBus) : Controller
{
    private static readonly TimeSpan YellowHours = TimeSpan.FromHours(48);
    private static readonly TimeSpan RedHours = TimeSpan.FromHours(24);

    /// <summary>Ресторан текущего пользователя по его AccessCode → OrganizationId.</summary>
    private async Task<int?> GetRestaurantIdAsync()
    {
        var codeIdStr = User.FindFirstValue("AccessCodeId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(codeIdStr, out var codeId)) return null;

        var orgId = await db.AccessCodes
            .Where(c => c.Id == codeId)
            .Select(c => (int?)c.OrganizationId)
            .FirstOrDefaultAsync();
        if (orgId is null) return null;

        return await db.Restaurants
            .Where(r => r.OrganizationId == orgId)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync();
    }

    private static string SemaphoreClass(DateTime expiresAt, DateTime now)
    {
        if (expiresAt <= now) return "sv-row-expired";
        var left = expiresAt - now;
        if (left < RedHours) return "sv-row-red";
        if (left < YellowHours) return "sv-row-yellow";
        return "sv-row-green";
    }

    private static string HoursLeftText(DateTime expiresAt, DateTime now)
    {
        var left = expiresAt - now;
        if (left <= TimeSpan.Zero) return "просрочено";
        var h = left.TotalHours;
        if (h >= 48) return $"{(int)h / 24} дн {(int)h % 24} ч";
        return $"{h:0.#} ч";
    }

    // GET /Inventory — дашборд
    public async Task<IActionResult> Index()
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        var now = DateTime.UtcNow;

        var batches = await db.Batches
            .Include(b => b.Product)
            .Include(b => b.Delivery)
            .Where(b => b.RestaurantId == rid && b.QtyRemaining > 0)
            .ToListAsync();

        var live = batches.Where(b => b.ExpiresAt > now).ToList();
        var expiring24 = live.Count(b => b.ExpiresAt < now.AddHours(24));

        var stopList = await orders.GetStopListAsync(rid.Value);

        // Hot offers: блюда, чьи ингредиенты частично горят (<24ч)
        var dishes = await db.Dishes
            .Where(d => d.RestaurantId == rid && d.IsActive)
            .Include(d => d.RecipeItems)
            .ToListAsync();

        var batchesByProduct = live
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.ExpiresAt).ToList());

        // Hot offers — из HotOffersService (заменяет заглушку-приоритет)
        var hotOffersList = await hotOffersService.GetTopAsync(rid.Value, 10);
        var dishById = dishes.ToDictionary(d => d.Id);
        double HoursLeftOf(int dishId) => dishById.GetValueOrDefault(dishId)?.RecipeItems
            .Select(ri => batchesByProduct.GetValueOrDefault(ri.ProductId)?.FirstOrDefault()?.ExpiresAt)
            .Where(e => e.HasValue)
            .Select(e => (e!.Value - now).TotalHours)
            .DefaultIfEmpty(double.MaxValue)
            .Min() ?? double.MaxValue;

        var recommended = hotOffersList.Take(5).Select(h => new
        {
            Name = h.DishName,
            Price = dishById.GetValueOrDefault(h.DishId)?.Price ?? 0m,
            h.Score,
            h.Reason,
            HoursLeft = HoursLeftOf(h.DishId),
        }).ToList();

        var burning = live
            .OrderBy(b => b.ExpiresAt)
            .Take(5)
            .Select(b => new
            {
                Product = b.Product!.Name,
                Delivery = b.Delivery!.Number,
                b.QtyRemaining,
                b.ExpiresAt,
                RowClass = SemaphoreClass(b.ExpiresAt, now),
                HoursText = HoursLeftText(b.ExpiresAt, now),
            })
            .ToList();

        ViewBag.KpiPositions = live.Select(b => b.ProductId).Distinct().Count();
        ViewBag.KpiExpiring = expiring24;
        ViewBag.KpiStopList = stopList.Count;
        ViewBag.KpiHotOffers = hotOffersList.Count;
        ViewBag.Burning = burning;
        ViewBag.Recommended = recommended;
        ViewBag.Now = now;
        // Для пульса и приветствия
        ViewBag.HasAlert = stopList.Count > 0 || live.Any(b => b.ExpiresAt < now.AddHours(12));
        ViewBag.ThreatCount = live.Count(b => b.ExpiresAt < now.AddHours(24));

        // Герой: ближайшая партия (продукт, срок, масса, оценка ₽) — одна мысль
        var next = live.OrderBy(b => b.ExpiresAt).FirstOrDefault();
        if (next is not null)
        {
            var price = next.Product?.PurchasePrice ?? 0;
            ViewBag.NextExpiryProduct = next.Product?.Name;
            ViewBag.NextExpiryHours = (next.ExpiresAt - now).TotalHours;
            ViewBag.NextExpiryQty = next.QtyRemaining;
            ViewBag.NextExpiryUnit = next.Product?.Unit;
            ViewBag.NextExpiryRub = Math.Round(next.QtyRemaining * price, 0);
        }

        // До конца текущей смены (виджет «Смена» в сайдбаре — синхронно)
        var todayShift = await db.Shifts.AsNoTracking()
            .Where(s => s.RestaurantId == rid && s.Date == DateOnly.FromDateTime(now))
            .OrderByDescending(s => s.EndTime)
            .Select(s => (TimeOnly?)s.EndTime)
            .FirstOrDefaultAsync();
        if (todayShift is TimeOnly endTime)
            ViewBag.ShiftEndLocal = endTime;

        // ИИ-анализ смены — текстовое резюме (fallback на локальные правила)
        ViewBag.AiSummary = await advisorService.GetSummaryAsync(rid.Value);
        return View();
    }

    // GET /Inventory/ChartData/river — партии для «реки свежести»
    [HttpGet]
    public async Task<IActionResult> ChartDataRiver()
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        var now = DateTime.UtcNow;

        var batches = await db.Batches
            .Include(b => b.Product)
            .Include(b => b.Delivery)
            .Where(b => b.RestaurantId == rid && b.QtyRemaining > 0 && b.ExpiresAt > now)
            .OrderBy(b => b.ExpiresAt)
            .Take(12)
            .Select(b => new
            {
                name = b.Product!.Name,
                delivery = b.Delivery!.Number,
                qty = b.QtyRemaining,
                unit = b.Product!.Unit,
                hoursLeft = Math.Round((b.ExpiresAt - now).TotalHours, 1),
            })
            .ToListAsync();

        return Json(batches);
    }

    // GET /Inventory/ChartData/orderPulse — заказы по часам за последние 24 ч + вчера для сравнения
    [HttpGet]
    public async Task<IActionResult> ChartDataOrderPulse()
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        var now = DateTime.UtcNow;
        var from = now.AddHours(-24);
        var fromYesterday = from.AddDays(-1);

        var rows = await db.Orders
            .Where(o => o.RestaurantId == rid && o.CreatedAt >= fromYesterday)
            .Select(o => o.CreatedAt)
            .ToListAsync();

        // bucket по часам: сегодня и те же часы сутки назад
        var buckets = new int[24];
        var yesterday = new int[24];
        for (var i = 0; i < 24; i++)
        {
            var bucketStart = from.AddHours(i);
            buckets[i] = rows.Count(t => t >= bucketStart && t < bucketStart.AddHours(1));
            var yStart = bucketStart.AddDays(-1);
            yesterday[i] = rows.Count(t => t >= yStart && t < yStart.AddHours(1));
        }

        var labels = Enumerable.Range(0, 24)
            .Select(i => from.AddHours(i).ToLocalTime().ToString("HH:mm"))
            .ToList();

        return Json(new { labels, values = buckets, yesterday });
    }

    // GET /Inventory/ChartData/waterfall — водопад списаний за сегодня
    [HttpGet]
    public async Task<IActionResult> ChartDataWaterfall()
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        var now = DateTime.UtcNow;
        var dayStart = now.Date;

        var startQty = await db.Batches
            .Where(b => b.RestaurantId == rid)
            .SumAsync(b => b.QtyInitial);

        var writeOrder = await db.WriteOffs
            .Where(w => w.RestaurantId == rid && w.Reason == "order" && w.CreatedAt >= dayStart)
            .SumAsync(w => (decimal?)w.Qty) ?? 0;
        var writeExpired = await db.WriteOffs
            .Where(w => w.RestaurantId == rid && w.Reason == "expired" && w.CreatedAt >= dayStart)
            .SumAsync(w => (decimal?)w.Qty) ?? 0;

        var currentQty = await db.Batches
            .Where(b => b.RestaurantId == rid && b.QtyRemaining > 0 && b.ExpiresAt > now)
            .SumAsync(b => b.QtyRemaining);

        return Json(new
        {
            start = Math.Round(startQty, 2),
            byOrder = Math.Round(writeOrder, 2),
            byExpired = Math.Round(writeExpired, 2),
            current = Math.Round(currentQty, 2),
        });
    }

    // GET /Inventory/ChartData/expiryHeatmap — карта просрочки: дни × продукты
    [HttpGet]
    public async Task<IActionResult> ChartDataExpiryHeatmap()
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(7);

        var rows = await db.Batches
            .Include(b => b.Product)
            .Where(b => b.RestaurantId == rid && b.QtyRemaining > 0 && b.ExpiresAt > now && b.ExpiresAt <= horizon)
            .GroupBy(b => new { b.Product!.Name, Day = b.ExpiresAt.Date })
            .Select(g => new
            {
                product = g.Key.Name,
                day = g.Key.Day,
                qty = g.Sum(b => b.QtyRemaining),
            })
            .ToListAsync();

        var products = rows.Select(r => r.product).Distinct().OrderBy(x => x).ToList();
        var days = Enumerable.Range(0, 7).Select(i => now.Date.AddDays(i)).ToList();

        // heatmap-матрица для ECharts: [x=день, y=продукт, value=qty]
        var data = rows.Select(r => new object[]
        {
            days.FindIndex(d => d == r.day),
            products.IndexOf(r.product),
            Math.Round(r.qty, 2),
        }).ToList();

        return Json(new
        {
            products,
            days = days.Select(d => d.ToString("dd.MM")).ToList(),
            data,
        });
    }

    // GET /Inventory/Stock?expiringOnly=true&category= — остатки по партиям
    public async Task<IActionResult> Stock(bool expiringOnly = false, string? category = null)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        var now = DateTime.UtcNow;

        var query = db.Batches
            .Include(b => b.Product)
            .Include(b => b.Delivery)
            .Where(b => b.RestaurantId == rid && b.QtyRemaining > 0);

        if (expiringOnly)
            query = query.Where(b => b.ExpiresAt > now && b.ExpiresAt < now.AddHours(48));
        if (!string.IsNullOrEmpty(category))
            query = query.Where(b => b.Product!.Category == category);

        var batches = await query.OrderBy(b => b.ExpiresAt).ToListAsync();

        // Группы по продукту (каждая партия — отдельная строка внутри группы, FEFO)
        var groups = batches
            .GroupBy(b => b.ProductId)
            .Select(g => new
            {
                Product = g.First().Product!.Name,
                Category = g.First().Product.Category,
                Unit = g.First().Product.Unit,
                Total = g.Sum(b => b.QtyRemaining),
                Earliest = g.Min(b => b.ExpiresAt),
                Batches = g.OrderBy(b => b.ExpiresAt).Select(b => new
                {
                    b.Id,
                    Delivery = b.Delivery!.Number,
                    b.QtyInitial,
                    b.QtyRemaining,
                    b.ExpiresAt,
                    RowClass = SemaphoreClass(b.ExpiresAt, now),
                    HoursText = HoursLeftText(b.ExpiresAt, now),
                }).ToList(),
            })
            .OrderBy(g => g.Earliest)
            .ToList();

        // Категории для фильтра-чипов
        ViewBag.Categories = await db.Products.AsNoTracking()
            .Where(p => p.RestaurantId == rid)
            .Select(p => p.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
        ViewBag.ExpiringOnly = expiringOnly;
        ViewBag.Category = category;
        ViewBag.Groups = groups;
        // Плоский список для счётчиков-ламп
        ViewBag.Stock = groups.SelectMany(g => g.Batches).ToList();
        return View();
    }

    // POST /Inventory/WriteOff — ручное списание партии (порча раньше срока)
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WriteOff(int batchId, decimal qty, string reason, string? comment)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();

        var batch = await db.Batches
            .Include(b => b.Product)
            .FirstOrDefaultAsync(b => b.Id == batchId && b.RestaurantId == rid);
        if (batch is null) return NotFound();

        if (qty <= 0 || qty > batch.QtyRemaining)
        {
            TempData["StockError"] = $"Некорректное количество — на складе {batch.QtyRemaining:0.##} {batch.Product.Unit}.";
            return RedirectToAction(nameof(Stock));
        }

        batch.QtyRemaining -= qty;
        db.WriteOffs.Add(new WriteOff
        {
            RestaurantId = rid.Value,
            BatchId = batch.Id,
            ProductId = batch.ProductId,
            Qty = qty,
            Reason = string.IsNullOrWhiteSpace(reason) ? "manual" : reason.Trim().ToLower(),
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await eventBus.PublishAsync(InventoryEventBus.StockChanged, rid.Value);

        TempData["StockOk"] = $"Списано {qty:0.##} {batch.Product.Unit} «{batch.Product.Name}» ({reason}).";
        return RedirectToAction(nameof(Stock));
    }

    // POST /Inventory/ImportProductsPreview — загрузил файл → предпросмотр с валидацией
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportProductsPreview(IFormFile? file)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();

        if (file is null || file.Length == 0)
        {
            TempData["DeliveryError"] = "Файл не выбран.";
            return RedirectToAction(nameof(Deliveries));
        }

        List<ProductImportService.ImportRow> rows;
        await using (var stream = file.OpenReadStream())
            rows = ProductImportService.Parse(stream, file.FileName);

        if (rows.Count == 0)
        {
            TempData["DeliveryError"] = "В файле нет строк с продуктами.";
            return RedirectToAction(nameof(Deliveries));
        }

        var existing = await db.Products.AsNoTracking()
            .Where(p => p.RestaurantId == rid)
            .Select(p => p.Name)
            .ToListAsync();
        ProductImportService.Validate(rows, existing);

        // Строки передаются обратно через форму в скрытом JSON — пользователь правит в таблице
        ViewBag.ImportRows = rows;
        ViewBag.ImportJson = ProductImportService.SerializeRows(rows);
        return await DeliveriesView(rid.Value, null);
    }

    // POST /Inventory/ImportProductsConfirm — пользователь подтвердил → создаём продукты
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportProductsConfirm(string importJson)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();

        var rows = ProductImportService.DeserializeRows(importJson);
        var existing = await db.Products.AsNoTracking()
            .Where(p => p.RestaurantId == rid)
            .Select(p => p.Name)
            .ToListAsync();
        ProductImportService.Validate(rows, existing);

        // Импортируем только валидные строки
        var valid = rows.Where(r => r.Errors.Count == 0).ToList();
        if (valid.Count == 0)
        {
            TempData["DeliveryError"] = "После правок не осталось валидных строк — ничего не импортировано.";
            return RedirectToAction(nameof(Deliveries));
        }

        foreach (var r in valid)
        {
            db.Products.Add(new Product
            {
                RestaurantId = rid.Value,
                Name = r.Name.Trim(),
                Category = string.IsNullOrWhiteSpace(r.Category) ? "Прочее" : r.Category.Trim(),
                Unit = r.Unit.Trim(),
                PurchasePrice = r.PurchasePrice,
            });
        }
        await db.SaveChangesAsync();
        TempData["DeliveryOk"] = $"Импортировано {valid.Count} продуктов.";
        return RedirectToAction(nameof(Deliveries));
    }

    // GET /Inventory/Deliveries?month=2026-10 — журнал по месяцам + форма приёма
    public async Task<IActionResult> Deliveries(string? month = null)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        return await DeliveriesView(rid.Value, month);
    }

    // Общий рендер страницы Поставки — вызывается и из GET, и из предпросмотра импорта.
    private async Task<IActionResult> DeliveriesView(int rid, string? month)
    {
        var all = await db.Deliveries
            .Where(d => d.RestaurantId == rid)
            .Include(d => d.Batches).ThenInclude(b => b.Product)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync();

        // Вкладки по месяцам: «Текущий месяц» + все месяцы с поставками
        var monthGroups = all
            .GroupBy(d => new DateTime(d.CreatedAt.Year, d.CreatedAt.Month, 1))
            .OrderByDescending(g => g.Key)
            .ToList();
        var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        // Активный месяц: по умолчанию — текущий (или первый имеющийся)
        DateTime? activeMonth = null;
        if (!string.IsNullOrEmpty(month) && DateTime.TryParse(month + "-01", out var m)) activeMonth = m;
        else if (month == "current" || month is null) activeMonth = monthGroups.Any(g => g.Key == currentMonth) ? currentMonth : monthGroups.FirstOrDefault()?.Key;

        var deliveries = activeMonth.HasValue
            ? monthGroups.Where(g => g.Key == activeMonth).SelectMany(g => g).ToList()
            : all;

        var products = await db.Products
            .Where(p => p.RestaurantId == rid)
            .OrderBy(p => p.Category).ThenBy(p => p.Name)
            .ToListAsync();

        // Сумма поставки по закупочным ценам
        var sums = deliveries.ToDictionary(d => d.Id, d => d.Batches.Sum(b => b.QtyInitial * b.Product.PurchasePrice));

        ViewBag.Deliveries = deliveries;
        ViewBag.DeliverySums = sums;
        ViewBag.Products = products;
        ViewBag.Months = monthGroups.Select(g => g.Key).ToList();
        ViewBag.ActiveMonth = activeMonth;
        ViewBag.CurrentMonth = currentMonth;
        return View("Deliveries");
    }

    // POST /Inventory/Deliveries — приёмка поставки
    // productName: имя из datalist; productId — если выбран существующий.
    // Новый продукт создаётся инлайн по (productName + ncat + nunit).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deliveries(
        string number, int[] productId, string[] productName, string[] ncat, string[] nunit,
        decimal[] qty, double[] shelfHours)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();

        var lines = Math.Max(productId?.Length ?? 0, productName?.Length ?? 0);
        if (string.IsNullOrWhiteSpace(number) || lines == 0)
        {
            TempData["DeliveryError"] = "Укажите номер поставки и хотя бы одну позицию.";
            return RedirectToAction(nameof(Deliveries));
        }

        var now = DateTime.UtcNow;
        var delivery = new Delivery
        {
            RestaurantId = rid.Value,
            Number = number.Trim(),
            CreatedAt = now,
        };
        db.Deliveries.Add(delivery);

        // Кэш продуктов ресторана по имени — чтобы «создать новый» дал тот же Product
        var existing = await db.Products
            .Where(p => p.RestaurantId == rid)
            .ToDictionaryAsync(p => p.Name.Trim(), p => p, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        for (var i = 0; i < lines; i++)
        {
            if (i >= qty.Length || i >= shelfHours.Length || qty[i] <= 0) continue;

            var pid = productId is not null && i < productId.Length ? productId[i] : 0;
            var name = productName is not null && i < productName.Length ? (productName[i] ?? "").Trim() : "";

            // Поиск продукта: сначала по Id, потом по имени из datalist
            Product? product = null;
            if (pid > 0) product = await db.Products.FindAsync(pid);
            if (product is null && name.Length > 0 && existing.TryGetValue(name, out var found)) product = found;

            // Инлайн-создание нового продукта
            if (product is null && name.Length > 0)
            {
                var cat = ncat is not null && i < ncat.Length ? (ncat[i] ?? "").Trim() : "";
                var unt = nunit is not null && i < nunit.Length ? (nunit[i] ?? "").Trim() : "";
                product = new Product
                {
                    RestaurantId = rid.Value,
                    Name = name,
                    Category = cat.Length > 0 ? cat : "Прочее",
                    Unit = unt.Length > 0 ? unt : "шт",
                };
                db.Products.Add(product);
                existing[name] = product;
            }
            if (product is null) continue;

            db.Batches.Add(new Batch
            {
                RestaurantId = rid.Value,
                Delivery = delivery,
                Product = product,
                QtyInitial = qty[i],
                QtyRemaining = qty[i],
                ReceivedAt = now,
                ExpiresAt = now.AddHours(shelfHours[i]),
            });
            added++;
        }

        if (added == 0)
        {
            TempData["DeliveryError"] = "Ни одной валидной позиции — укажите продукт и количество.";
            return RedirectToAction(nameof(Deliveries));
        }

        await db.SaveChangesAsync();
        await eventBus.PublishAsync(InventoryEventBus.StockChanged, rid.Value);
        TempData["DeliveryOk"] = $"Поставка {delivery.Number} принята ({added} поз.).";
        return RedirectToAction(nameof(Deliveries));
    }

    // GET /Inventory/Order — форма заказа + журнал (сегодня) + архив по датам
    public async Task<IActionResult> Order(bool archive = false)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        var now = DateTime.UtcNow;
        var today = now.Date;

        var dishes = await db.Dishes
            .Where(d => d.RestaurantId == rid && d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync();

        var all = await db.Orders
            .Where(o => o.RestaurantId == rid)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.Status,
                o.RejectReason,
                o.CreatedAt,
                ItemCount = o.Items.Sum(i => i.Qty),
                Sum = o.Items.Sum(i => i.Qty * i.Dish.Price),
            })
            .ToListAsync();

        // Сегодня — оперативный журнал; старше — в архив, сгруппировано по датам
        var todayOrders = all.Where(o => o.CreatedAt >= today).ToList();
        var archiveGroups = all
            .Where(o => o.CreatedAt < today)
            .GroupBy(o => o.CreatedAt.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => new
            {
                Date = g.Key,
                Orders = g.OrderByDescending(o => o.CreatedAt).ToList(),
                Accepted = g.Count(o => o.Status == "accepted"),
                Rejected = g.Count(o => o.Status != "accepted"),
                Sum = g.Where(o => o.Status == "accepted").Sum(o => o.Sum),
            })
            .ToList();

        ViewBag.Dishes = dishes;
        ViewBag.Journal = todayOrders;
        ViewBag.Archive = archiveGroups;
        ViewBag.ShowArchive = archive;
        return View();
    }

    // POST /Inventory/Order — оформление
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Order(int[] dishId, int[] qty)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();

        var items = dishId.Zip(qty, (d, q) => (DishId: d, Qty: q))
            .Where(x => x.Qty > 0)
            .Select(x => (x.DishId, x.Qty))
            .ToList();

        if (items.Count == 0)
        {
            TempData["OrderError"] = "Выберите хотя бы одно блюдо.";
            return RedirectToAction(nameof(Order));
        }

        var result = await orders.PlaceOrderAsync(rid.Value, items);
        await eventBus.PublishAsync(InventoryEventBus.OrderCreated, rid.Value);
        if (result.Status == OrderService.StatusRejected)
        {
            TempData["OrderError"] = result.RejectReason;
        }
        else
        {
            await eventBus.PublishAsync(InventoryEventBus.StockChanged, rid.Value);
            TempData["OrderOk"] = $"Заказ #{result.OrderId} принят — списания проведены по FEFO.";
        }
        return RedirectToAction(nameof(Order));
    }

    // POST /Inventory/StopToggle — ручной стоп / возврат блюда
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StopToggle(int dishId, string? stopComment, string? returnTo)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();

        var dish = await db.Dishes.FirstOrDefaultAsync(d => d.Id == dishId && d.RestaurantId == rid);
        if (dish is null) return NotFound();

        if (dish.ManualStopped)
        {
            dish.ManualStopped = false;
            dish.StopComment = null;
            TempData["OrderOk"] = $"«{dish.Name}» возвращено в меню.";
        }
        else
        {
            dish.ManualStopped = true;
            dish.StopComment = string.IsNullOrWhiteSpace(stopComment) ? null : stopComment.Trim();
            TempData["OrderOk"] = $"«{dish.Name}» остановлено вручную.";
        }
        await db.SaveChangesAsync();
        await eventBus.PublishAsync(InventoryEventBus.StockChanged, rid.Value);

        return RedirectToAction(returnTo == "order" ? nameof(Order) : nameof(StopList));
    }

    // GET /Inventory/StopList — стоп-лист + hot offers
    public async Task<IActionResult> StopList()
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        var now = DateTime.UtcNow;

        var stopList = await orders.GetStopListAsync(rid.Value);

        var dishes = await db.Dishes
            .Where(d => d.RestaurantId == rid && d.IsActive)
            .Include(d => d.RecipeItems).ThenInclude(r => r.Product)
            .ToListAsync();

        var liveBatches = await db.Batches
            .Where(b => b.RestaurantId == rid && b.QtyRemaining > 0 && b.ExpiresAt > now)
            .ToListAsync();
        var batchesByProduct = liveBatches
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.ExpiresAt).ToList());

        // Hot offers — из HotOffersService с человекочитаемым объяснением
        var hotOffersList = await hotOffersService.GetTopAsync(rid.Value, 10);
        var dishById = dishes.ToDictionary(d => d.Id);
        double HoursLeftOf(int dishId) => dishById.GetValueOrDefault(dishId)?.RecipeItems
            .Select(ri => batchesByProduct.GetValueOrDefault(ri.ProductId)?.FirstOrDefault()?.ExpiresAt)
            .Where(e => e.HasValue)
            .Select(e => (e!.Value - now).TotalHours)
            .DefaultIfEmpty(double.MaxValue)
            .Min() ?? double.MaxValue;

        var hotOffers = hotOffersList.Select(h => new
        {
            Name = h.DishName,
            Price = dishById.GetValueOrDefault(h.DishId)?.Price ?? 0m,
            h.Score,
            h.Reason,
            HoursLeft = HoursLeftOf(h.DishId),
        }).ToList();

        ViewBag.StopList = stopList;
        ViewBag.HotOffers = hotOffers;
        // Все активные блюда — для кнопок «В стоп»/«Вернуть»
        ViewBag.Dishes = dishes.Select(d => new { d.Id, d.Name, d.ManualStopped }).ToList();
        return View();
    }

    // GET /Inventory/Notifications — «Центр смены»: события смен, мотивация, алерты
    [HttpGet]
    public async Task<IActionResult> Notifications()
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var time = TimeOnly.FromDateTime(now);
        var items = new List<object>();

        // --- (а) События смен ---
        var todayShifts = await db.Shifts.AsNoTracking()
            .Include(s => s.AccessCode)
            .Where(s => s.RestaurantId == rid && s.Date == today)
            .OrderBy(s => s.StartTime)
            .ToListAsync();
        foreach (var s in todayShifts)
        {
            var active = s.StartTime <= time && s.EndTime >= time;
            var name = s.AccessCode?.EmployeeName ?? "Сотрудник";
            if (active)
            {
                var leftH = (s.EndTime.ToTimeSpan() - time.ToTimeSpan()).TotalHours;
                items.Add(new { kind = "shift", icon = "st-check.png", title = $"Смена {name} ({s.Position}) до {s.EndTime:HH\\:mm}", text = $"осталось {Math.Max(0, leftH):0.#} ч", severity = "low", url = "/Admin/Schedule" });
            }
            else if (s.StartTime > time)
            {
                items.Add(new { kind = "shift", icon = "st-check.png", title = $"{name} заступает в {s.StartTime:HH\\:mm}", text = s.Position, severity = "low", url = "/Admin/Schedule" });
            }
        }

        // --- (б) Мотивация ---
        // Спасено от списания за сегодня = заказы, проведённые по FEFO из горящих партий
        var savedQty = await db.WriteOffs
            .Where(w => w.RestaurantId == rid && w.Reason == OrderService.ReasonOrder && w.CreatedAt >= today.ToDateTime(TimeOnly.MinValue))
            .SumAsync(w => (decimal?)w.Qty) ?? 0;
        var savedRub = await db.WriteOffs
            .Where(w => w.RestaurantId == rid && w.Reason == OrderService.ReasonOrder && w.CreatedAt >= today.ToDateTime(TimeOnly.MinValue))
            .Include(w => w.Product)
            .SumAsync(w => (decimal?)(w.Qty * w.Product.PurchasePrice)) ?? 0;
        if (savedQty > 0)
            items.Add(new { kind = "motivation", icon = "st-check.png", title = "Сегодня спасено от списания", text = $"{savedQty:0.##} кг · ~{savedRub:N0} ₽", severity = "low", url = "/Inventory/Order" });

        // Часов без стоп-листа
        var stop = await orders.GetStopListAsync(rid.Value);
        var hoursFree = stop.Count == 0 ? (int)Math.Max(0, (now - today.ToDateTime(TimeOnly.MinValue)).TotalHours) : 0;
        if (stop.Count == 0 && hoursFree > 0)
            items.Add(new { kind = "motivation", icon = "st-check.png", title = $"{hoursFree} ч без стоп-листа", text = "кухня отдаёт всё меню", severity = "low", url = "/Inventory/StopList" });

        // Доля потерь
        var dayStart = today.ToDateTime(TimeOnly.MinValue);
        var startQty = await db.Batches.Where(b => b.RestaurantId == rid).SumAsync(b => b.QtyInitial);
        var expiredQty = await db.WriteOffs.Where(w => w.RestaurantId == rid && w.Reason == OrderService.ReasonExpired && w.CreatedAt >= dayStart).SumAsync(w => (decimal?)w.Qty) ?? 0;
        if (startQty > 0 && expiredQty > 0)
        {
            var pct = Math.Round(expiredQty / startQty * 100, 1);
            items.Add(new { kind = "motivation", icon = "st-hourglass.png", title = $"Доля потерь {pct:0.#}%", text = "цель ≤ 5%", severity = pct > 5 ? "medium" : "low", url = "/Inventory" });
        }

        // --- (в) Важные алерты ---
        var expiring = await db.Batches
            .Include(b => b.Product)
            .Where(b => b.RestaurantId == rid && b.QtyRemaining > 0 && b.ExpiresAt > now && b.ExpiresAt <= now.AddHours(12))
            .OrderBy(b => b.ExpiresAt)
            .Take(4)
            .Select(b => new { name = b.Product!.Name, hours = (b.ExpiresAt - now).TotalHours, qty = b.QtyRemaining, unit = b.Product.Unit })
            .ToListAsync();
        foreach (var e in expiring)
            items.Add(new { kind = "expiring", icon = "st-hourglass.png", title = $"«{e.name}» истекает", text = $"{e.hours:0.#} ч · {e.qty:0.##} {e.unit}", severity = e.hours < 4 ? "high" : "medium", url = "/Inventory/Stock?expiringOnly=true" });

        foreach (var s in stop.Take(3))
            items.Add(new { kind = "stop", icon = "st-stop.png", title = $"«{s.DishName}» в стоп-листе", text = "не хватает ингредиентов", severity = "high", url = "/Inventory/StopList" });

        var wroteOff = await db.WriteOffs
            .Where(w => w.RestaurantId == rid && w.Reason == OrderService.ReasonExpired && w.CreatedAt >= now.AddHours(-24))
            .SumAsync(w => (decimal?)w.Qty) ?? 0;
        if (wroteOff > 0)
            items.Add(new { kind = "writeoff", icon = "st-box.png", title = "Списано просроченным", text = $"{wroteOff:0.##} кг за сутки", severity = "medium", url = "/Inventory/Stock" });

        return Json(new { count = items.Count, generatedAtUtc = now, items });
    }

    // GET /Inventory/Search?q= — command-palette: блюда/продукты/поставки
    [HttpGet]
    public async Task<IActionResult> Search(string q)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        q = (q ?? "").Trim();
        if (q.Length < 2) return Json(new { results = Array.Empty<object>() });

        var lower = q.ToLower();
        var dishes = await db.Dishes.AsNoTracking()
            .Where(d => d.RestaurantId == rid && d.IsActive && d.Name.ToLower().Contains(lower))
            .Take(5).Select(d => new { type = "dish", name = d.Name, meta = $"{d.Price:0} ₽", url = "/Inventory/Order" }).ToListAsync();
        var products = await db.Products.AsNoTracking()
            .Where(p => p.RestaurantId == rid && p.Name.ToLower().Contains(lower))
            .Take(5).Select(p => new { type = "product", name = p.Name, meta = p.Category, url = "/Inventory/Stock" }).ToListAsync();
        var deliveries = await db.Deliveries.AsNoTracking()
            .Where(d => d.RestaurantId == rid && d.Number.ToLower().Contains(lower))
            .Take(5).Select(d => new { type = "delivery", name = d.Number, meta = "поставка", url = "/Inventory/Deliveries" }).ToListAsync();

        var results = dishes.Cast<object>().Concat(products).Concat(deliveries).ToList();
        return Json(new { results });
    }

    // POST /Inventory/ResetDemo — сброс демо-данных (только админ)
    [HttpPost]
    [AuthorizeRole(AppRole.Admin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetDemo()
    {
        await DbInitializer.ResetAndReseedAsync(services);
        TempData["OrderOk"] = "Демо-данные сброшены и заново загружены.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Служебные эндпоинты для event-генератора ----------
    // Доступны любому авторизованному пользователю (вход по access-коду).

    // GET /Inventory/Menu — список активных блюд и продуктов (для генератора)
    [HttpGet]
    public async Task<IActionResult> Menu()
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();

        var dishes = await db.Dishes
            .Where(d => d.RestaurantId == rid && d.IsActive)
            .Select(d => new { d.Id, d.Name, d.Price })
            .ToListAsync();
        var products = await db.Products
            .Where(p => p.RestaurantId == rid)
            .Select(p => new { p.Id, p.Name, p.Unit })
            .ToListAsync();

        return Json(new { restaurantId = rid, dishes, products });
    }

    // POST /Inventory/SimulateOrder — создать заказ от генератора
    // body: { items: [{dishId, qty}] }
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SimulateOrder([FromBody] SimulateOrderRequest req)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        if (req?.Items is null || req.Items.Count == 0)
            return BadRequest(new { error = "empty items" });

        var items = req.Items
            .Where(i => i.Qty > 0)
            .Select(i => (i.DishId, i.Qty))
            .ToList();
        if (items.Count == 0) return BadRequest(new { error = "no positive qty" });

        var result = await orders.PlaceOrderAsync(rid.Value, items);
        await eventBus.PublishAsync(InventoryEventBus.OrderCreated, rid.Value);
        if (result.Status == OrderService.StatusAccepted)
            await eventBus.PublishAsync(InventoryEventBus.StockChanged, rid.Value);

        return Json(new { result.OrderId, result.Status, result.RejectReason });
    }

    // POST /Inventory/SimulateDelivery — принять поставку от генератора
    // body: { number, items: [{productId, qty, shelfHours}] }
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> SimulateDelivery([FromBody] SimulateDeliveryRequest req)
    {
        var rid = await GetRestaurantIdAsync();
        if (rid is null) return Forbid();
        if (req?.Items is null || req.Items.Count == 0)
            return BadRequest(new { error = "empty items" });

        var now = DateTime.UtcNow;
        var delivery = new Delivery
        {
            RestaurantId = rid.Value,
            Number = string.IsNullOrWhiteSpace(req.Number) ? $"SIM-{now:HHmmss}" : req.Number.Trim(),
            CreatedAt = now,
        };
        db.Deliveries.Add(delivery);

        var created = 0;
        foreach (var it in req.Items)
        {
            if (it.Qty <= 0 || it.ShelfHours <= 0) continue;
            db.Batches.Add(new Batch
            {
                RestaurantId = rid.Value,
                Delivery = delivery,
                ProductId = it.ProductId,
                QtyInitial = it.Qty,
                QtyRemaining = it.Qty,
                ReceivedAt = now,
                ExpiresAt = now.AddHours(it.ShelfHours),
            });
            created++;
        }
        await db.SaveChangesAsync();
        await eventBus.PublishAsync(InventoryEventBus.StockChanged, rid.Value);

        return Json(new { deliveryId = delivery.Id, delivery.Number, created });
    }
}

public record SimulateOrderItem(int DishId, int Qty);
public record SimulateOrderRequest(List<SimulateOrderItem> Items);
public record SimulateDeliveryItem(int ProductId, decimal Qty, double ShelfHours);
public record SimulateDeliveryRequest(string? Number, List<SimulateDeliveryItem> Items);
