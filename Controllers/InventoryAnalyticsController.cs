using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Models.Inventory;
using SmartInventory.Services;

namespace SmartInventory.Controllers;

/// <summary>Restaurant-scoped analytics endpoints used by the Razor dashboards.</summary>
[Authorize]
[Route("Inventory/ChartData")]
public sealed class InventoryAnalyticsController(ApplicationDbContext db, OrderService orders) : Controller
{
    private sealed record ForecastRow(int DishId, string Dish, double DaysUntilStop, string LimitingProduct, decimal Stock, decimal DailyUse, bool AlreadyStopped);

    [HttpGet("stopForecast")]
    public async Task<IActionResult> StopForecast(CancellationToken ct)
    {
        var rid = await GetRestaurantIdAsync(ct);
        if (rid is null) return Forbid();
        var now = DateTime.UtcNow;
        var from = now.AddDays(-7);
        var dishes = await db.Dishes.AsNoTracking()
            .Where(d => d.RestaurantId == rid && d.IsActive)
            .Include(d => d.RecipeItems)
            .ToListAsync(ct);
        var dishById = dishes.ToDictionary(x => x.Id);
        var orders7d = await db.Orders.AsNoTracking()
            .Where(o => o.RestaurantId == rid && o.Status == OrderService.StatusAccepted && o.CreatedAt >= from)
            .Include(o => o.Items)
            .ToListAsync(ct);
        var use = new Dictionary<int, decimal>();
        foreach (var order in orders7d)
        foreach (var item in order.Items)
        {
            if (!dishById.TryGetValue(item.DishId, out var dish)) continue;
            foreach (var recipe in dish.RecipeItems)
                use[recipe.ProductId] = use.GetValueOrDefault(recipe.ProductId) + recipe.Quantity * item.Qty;
        }
        var productIds = dishes.SelectMany(x => x.RecipeItems).Select(x => x.ProductId).Distinct().ToList();
        var stockRows = await db.Batches.AsNoTracking()
            .Where(b => b.RestaurantId == rid && productIds.Contains(b.ProductId) && b.QtyRemaining > 0 && b.ExpiresAt > now)
            .GroupBy(b => b.ProductId)
            .Select(g => new { ProductId = g.Key, Qty = g.Sum(b => b.QtyRemaining) })
            .ToListAsync(ct);
        var stock = stockRows.ToDictionary(x => x.ProductId, x => x.Qty);
        var productNames = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var stopped = (await orders.GetStopListAsync(rid.Value, ct)).Select(x => x.DishId).ToHashSet();

        var result = new List<ForecastRow>();
        foreach (var dish in dishes)
        {
            var risks = dish.RecipeItems
                .Select(r => new { r.ProductId, Need = r.Quantity, Stock = stock.GetValueOrDefault(r.ProductId), Daily = use.GetValueOrDefault(r.ProductId) / 7m })
                .Where(x => x.Need > 0 && (x.Stock < x.Need || x.Daily > 0))
                .Select(x => new { x.ProductId, x.Stock, x.Daily, Days = x.Stock < x.Need ? 0d : x.Daily > 0 ? (double)(x.Stock / x.Daily) : double.PositiveInfinity })
                .OrderBy(x => x.Days)
                .FirstOrDefault();
            if (risks is null || double.IsPositiveInfinity(risks.Days)) continue;
            result.Add(new ForecastRow(dish.Id, dish.Name, Math.Round(risks.Days, 1), productNames.GetValueOrDefault(risks.ProductId, "Продукт"), risks.Stock, Math.Round(risks.Daily, 3), stopped.Contains(dish.Id)));
        }
        return Json(new
        {
            periodDays = 7,
            estimate = true,
            methodology = "Прогноз по среднему расходу принятых заказов за 7 дней; при нулевой истории прогноз не строится.",
            dishes = result.OrderBy(x => x.DaysUntilStop).Take(20)
        });
    }

    [HttpGet("writeoffRisk")]
    public async Task<IActionResult> WriteoffRisk(CancellationToken ct)
    {
        var rid = await GetRestaurantIdAsync(ct);
        if (rid is null) return Forbid();
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(7);
        var dishes = await db.Dishes.AsNoTracking().Where(d => d.RestaurantId == rid && d.IsActive)
            .Include(d => d.RecipeItems).ToListAsync(ct);
        var productIds = dishes.SelectMany(d => d.RecipeItems).Select(r => r.ProductId).Distinct().ToList();
        var unitValues = new Dictionary<int, decimal>();
        foreach (var productId in productIds)
        {
            var values = dishes.SelectMany(d => d.RecipeItems.Where(r => r.ProductId == productId && r.Quantity > 0)
                .Select(r => d.Price / r.Quantity)).ToList();
            if (values.Count > 0) unitValues[productId] = values.Average();
        }
        var batches = await db.Batches.AsNoTracking().Include(b => b.Product).Include(b => b.Delivery)
            .Where(b => b.RestaurantId == rid && b.QtyRemaining > 0 && b.ExpiresAt > now && b.ExpiresAt <= horizon)
            .OrderBy(b => b.ExpiresAt).ToListAsync(ct);
        var risks = batches.Select(b => new
        {
            product = b.Product!.Name,
            unit = b.Product.Unit,
            delivery = b.Delivery!.Number,
            qty = b.QtyRemaining,
            hoursLeft = Math.Round((b.ExpiresAt - now).TotalHours, 1),
            estimatedValueRub = Math.Round(b.QtyRemaining * unitValues.GetValueOrDefault(b.ProductId), 2)
        }).ToList();
        return Json(new
        {
            estimate = true,
            methodology = "Оценка по средней розничной цене блюда, делённой на норму ингредиента; фактическая закупочная себестоимость в модели не хранится.",
            totalRiskRub = risks.Sum(x => x.estimatedValueRub),
            batches = risks
        });
    }

    [HttpGet("lossShare")]
    public async Task<IActionResult> LossShare(CancellationToken ct)
    {
        var rid = await GetRestaurantIdAsync(ct);
        if (rid is null) return Forbid();
        var from = DateTime.UtcNow.AddDays(-30);
        var dishes = await db.Dishes.AsNoTracking().Where(d => d.RestaurantId == rid && d.IsActive)
            .Include(d => d.RecipeItems).ToListAsync(ct);
        var values = dishes.SelectMany(d => d.RecipeItems.Where(r => r.Quantity > 0).Select(r => new { r.ProductId, Value = d.Price / r.Quantity })).GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Average(x => x.Value));
        var writeoffs = await db.WriteOffs.AsNoTracking().Where(w => w.RestaurantId == rid && w.CreatedAt >= from)
            .GroupBy(w => new { w.Reason, w.ProductId }).Select(g => new { g.Key.Reason, g.Key.ProductId, Qty = g.Sum(w => w.Qty) }).ToListAsync(ct);
        var remaining = await db.Batches.AsNoTracking().Where(b => b.RestaurantId == rid && b.QtyRemaining > 0 && b.ExpiresAt > DateTime.UtcNow)
            .GroupBy(b => b.ProductId).Select(g => new { ProductId = g.Key, Qty = g.Sum(b => b.QtyRemaining) }).ToListAsync(ct);
        var sold = writeoffs.Where(x => x.Reason == OrderService.ReasonOrder).Sum(x => x.Qty * values.GetValueOrDefault(x.ProductId));
        var expired = writeoffs.Where(x => x.Reason == OrderService.ReasonExpired).Sum(x => x.Qty * values.GetValueOrDefault(x.ProductId));
        var stockValue = remaining.Sum(x => x.Qty * values.GetValueOrDefault(x.ProductId));
        return Json(new
        {
            periodDays = 30,
            estimate = true,
            methodology = "Эквивалент розничной стоимости по средней цене меню на единицу ингредиента; значения — оценка, не закупочная себестоимость.",
            labels = new[] { "Использовано в заказах", "Списано просроченным", "Остаток на складе" },
            values = new[] { Math.Round(sold, 2), Math.Round(expired, 2), Math.Round(stockValue, 2) }
        });
    }

    [HttpGet("productionHeatmap")]
    public async Task<IActionResult> ProductionHeatmap(CancellationToken ct)
    {
        var rid = await GetRestaurantIdAsync(ct);
        if (rid is null) return Forbid();
        var now = DateTime.UtcNow;
        var start = now.Date.AddDays(-6);
        var rows = await db.WriteOffs.AsNoTracking().Include(w => w.Product)
            .Where(w => w.RestaurantId == rid && w.Reason == OrderService.ReasonOrder && w.CreatedAt >= start)
            .Select(w => new { w.ProductId, Product = w.Product!.Name, w.Qty, w.CreatedAt })
            .ToListAsync(ct);
        var products = rows.GroupBy(x => x.ProductId).OrderByDescending(g => g.Sum(x => x.Qty)).Take(12).Select(g => g.First().Product).ToList();
        var days = Enumerable.Range(0, 7).Select(i => start.AddDays(i)).ToList();
        var data = rows.Where(r => products.Contains(r.Product)).GroupBy(r => new { r.Product, Day = r.CreatedAt.Date })
            .Select(g => new object[] { days.FindIndex(d => d == g.Key.Day), products.IndexOf(g.Key.Product), Math.Round(g.Sum(x => x.Qty), 2) }).ToList();
        return Json(new { products, days = days.Select(d => d.ToString("dd.MM")).ToList(), data });
    }

    private async Task<int?> GetRestaurantIdAsync(CancellationToken ct)
    {
        var value = User.FindFirstValue("AccessCodeId") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(value, out var id)) return null;
        var organizationId = await db.AccessCodes.AsNoTracking().Where(x => x.Id == id).Select(x => (int?)x.OrganizationId).FirstOrDefaultAsync(ct);
        if (organizationId is null) return null;
        return await db.Restaurants.AsNoTracking().Where(x => x.OrganizationId == organizationId).Select(x => (int?)x.Id).FirstOrDefaultAsync(ct);
    }
}
