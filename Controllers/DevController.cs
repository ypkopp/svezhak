using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Services;

namespace SmartInventory.Controllers;

/// <summary>
/// Временный дев-харнесс для ручной проверки OrderService.
/// Мапится ТОЛЬКО в Development (см. Program.cs).
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public class DevController(ApplicationDbContext db, OrderService orders) : Controller
{
    // GET /Dev/SeedStatus — сводка наполненности БД
    [HttpGet]
    public async Task<IActionResult> SeedStatus()
    {
        return Json(new
        {
            products = await db.Products.CountAsync(),
            batches = await db.Batches.CountAsync(),
            dishes = await db.Dishes.CountAsync(),
            recipeItems = await db.RecipeItems.CountAsync(),
            deliveries = await db.Deliveries.CountAsync(),
            orders = await db.Orders.CountAsync(),
            writeOffs = await db.WriteOffs.CountAsync(),
            restaurants = await db.Restaurants.CountAsync(),
            accessCodes = await db.AccessCodes.CountAsync(),
        });
    }

    // GET /Dev/Stock — остатки по партиям
    [HttpGet]
    public async Task<IActionResult> Stock()
    {
        var now = DateTime.UtcNow;
        var stock = await db.Batches
            .Include(b => b.Product)
            .Include(b => b.Delivery)
            .OrderBy(b => b.ProductId).ThenBy(b => b.ExpiresAt)
            .Select(b => new
            {
                b.Id,
                product = b.Product!.Name,
                delivery = b.Delivery!.Number,
                b.QtyInitial,
                b.QtyRemaining,
                b.ExpiresAt,
                hoursLeft = EF.Functions.DateDiffHour(now, b.ExpiresAt),
                expired = b.ExpiresAt <= now,
            })
            .ToListAsync();

        return Json(stock);
    }

    // POST /Dev/TestOrder?dishId=1&qty=2 (или dishName=...) — прогон PlaceOrderAsync
    [HttpPost]
    public async Task<IActionResult> TestOrder(string? dishName, int? dishId, int qty)
    {
        var dish = dishId.HasValue
            ? await db.Dishes.FirstOrDefaultAsync(d => d.Id == dishId.Value)
            : await db.Dishes.FirstOrDefaultAsync(d => d.Name == dishName);
        if (dish is null)
            return BadRequest(new { error = $"Блюдо '{dishName ?? $"#{dishId}"}' не найдено" });

        var result = await orders.PlaceOrderAsync(dish.RestaurantId, [(dish.Id, qty)]);

        var writeOffs = await db.WriteOffs
            .Where(w => w.OrderId == result.OrderId)
            .Include(w => w.Batch).ThenInclude(b => b.Delivery)
            .Include(w => w.Product)
            .Select(w => new
            {
                w.Product!.Name,
                w.Batch!.Delivery!.Number,
                w.BatchId,
                w.Qty,
            })
            .ToListAsync();

        return Json(new
        {
            result.OrderId,
            result.Status,
            result.RejectReason,
            writeOffs,
        });
    }

    // GET /Dev/StopList — текущий вычисляемый стоп-лист
    [HttpGet]
    public async Task<IActionResult> StopList()
    {
        var list = await orders.GetStopListAsync(1);
        return Json(list);
    }
}
