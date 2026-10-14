using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Services;

namespace SmartInventory.Controllers;

/// <summary>
/// Публичный терминал кухни: read-only стоп-лист по токену ресторана.
/// Без авторизации — монитор на кухне просто открывает QR-ссылку.
/// </summary>
[Route("Kitchen")]
public sealed class KitchenController(ApplicationDbContext db, OrderService orders) : Controller
{
    // GET /Kitchen/{token} — большой экран со стоп-листом, автообновление
    [HttpGet("{token:guid}")]
    public async Task<IActionResult> Index(Guid token)
    {
        var restaurant = await db.Restaurants.AsNoTracking()
            .FirstOrDefaultAsync(r => r.KitchenToken == token);
        if (restaurant is null) return NotFound();

        var stop = await orders.GetStopListAsync(restaurant.Id);
        ViewBag.Restaurant = restaurant;
        ViewBag.Token = token;
        ViewBag.StopList = stop;
        return View();
    }

    // GET /Kitchen/{token}/data — JSON для автообновления (polling)
    [HttpGet("{token:guid}/data")]
    public async Task<IActionResult> Data(Guid token)
    {
        var restaurant = await db.Restaurants.AsNoTracking()
            .FirstOrDefaultAsync(r => r.KitchenToken == token);
        if (restaurant is null) return NotFound();
        var stop = await orders.GetStopListAsync(restaurant.Id);
        return Json(new
        {
            at = DateTime.UtcNow,
            count = stop.Count,
            items = stop.Select(s => new { s.DishName, s.IsManual, s.Comment }),
        });
    }
}
