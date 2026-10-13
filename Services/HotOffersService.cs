using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Models.Inventory;

namespace SmartInventory.Services;

/// <summary>
/// Рекомендации Hot Offers: приоритизирует блюда, чьи ингредиенты скоро испортятся.
/// Просроченные партии (ExpiresAt &lt;= now) и блюда из стоп-листа игнорируются.
/// </summary>
public class HotOffersService(ApplicationDbContext db, OrderService orders)
{
    /// <summary>Карточка hot offer с объяснением.</summary>
    public record HotOffer(int DishId, string DishName, double Score, string Reason);

    private record ProductUrgency(
        int ProductId,
        string Name,
        string Unit,
        double HoursLeft,
        decimal StockQty,
        string DeliveryNumber,
        double Urgency);

    /// <summary>
    /// Топ hot offers по убыванию Score.
    /// </summary>
    /// <param name="restaurantId">Ресторан (мультитенантность).</param>
    /// <param name="count">Сколько позиций вернуть.</param>
    public async Task<List<HotOffer>> GetTopAsync(int restaurantId, int count = 10, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Активные блюда с рецептами (одна выборка, без N+1)
        var dishes = await db.Dishes
            .Where(d => d.RestaurantId == restaurantId && d.IsActive)
            .Include(d => d.RecipeItems)
            .ToListAsync(ct);

        if (dishes.Count == 0) return [];

        var productIds = dishes.SelectMany(d => d.RecipeItems).Select(r => r.ProductId).Distinct().ToList();

        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        // Живые партии (просроченные игнорируем), сгруппированные по продукту
        var liveBatches = await db.Batches
            .Include(b => b.Delivery)
            .Where(b => b.RestaurantId == restaurantId
                        && productIds.Contains(b.ProductId)
                        && b.QtyRemaining > 0
                        && b.ExpiresAt > now)
            .ToListAsync(ct);

        var batchesByProduct = liveBatches
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.ExpiresAt).ToList());

        // Суммарная потребность продукта по всем блюдам (на 1 порцию каждого)
        var totalRecipeQty = dishes
            .SelectMany(d => d.RecipeItems)
            .GroupBy(ri => ri.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(ri => ri.Quantity));

        // Urgency по каждому продукту: чем раньше срок и чем меньше запас
        // относительно потребности — тем срочнее.
        var urgencies = new Dictionary<int, ProductUrgency>();
        foreach (var pid in productIds)
        {
            var batches = batchesByProduct.GetValueOrDefault(pid);
            var product = products.GetValueOrDefault(pid);

            // Нет живых партий → продукт не считается «горящим» для hot offers
            if (batches is null || batches.Count == 0 || product is null)
            {
                urgencies[pid] = new ProductUrgency(pid, product?.Name ?? $"#{pid}", product?.Unit ?? "ед.", double.MaxValue, 0m, "", 0);
                continue;
            }

            var earliest = batches[0]; // уже отсортированы по ExpiresAt
            var hours = Math.Max(0.0001, (earliest.ExpiresAt - now).TotalHours);
            var stockQty = batches.Sum(b => b.QtyRemaining);
            var need = totalRecipeQty.GetValueOrDefault(pid);

            var timeFactor = 1.0 / Math.Max(1.0, hours);
            var scarcity = need <= 0 ? 0.0 : Math.Min(1.0, (double)(stockQty / need));
            var urgency = timeFactor * scarcity;

            urgencies[pid] = new ProductUrgency(pid, product.Name, product.Unit, hours, stockQty, earliest.Delivery?.Number ?? "", urgency);
        }

        // Стоп-лист — эти блюда исключаем из рекомендаций
        var stopList = await orders.GetStopListAsync(restaurantId, ct);
        var stopIds = stopList.Select(s => s.DishId).ToHashSet();

        var offers = new List<HotOffer>();
        foreach (var dish in dishes)
        {
            if (stopIds.Contains(dish.Id)) continue;
            if (dish.RecipeItems.Count == 0) continue;

            // Score = Σ urgency × норма расхода; только ингредиенты с живым сроком
            var burning = dish.RecipeItems
                .Select(ri => new { ri, u = urgencies.GetValueOrDefault(ri.ProductId) })
                .Where(x => x.u is not null && x.u.Urgency > 0)
                .OrderByDescending(x => x.u!.Urgency)
                .ToList();

            if (burning.Count == 0) continue; // все ингредиенты свежие/нет срока

            double score = dish.RecipeItems
                .Sum(ri => (urgencies.GetValueOrDefault(ri.ProductId)?.Urgency ?? 0) * (double)ri.Quantity);

            if (score <= 0) continue;

            // Топ-2 горящих ингредиента для объяснения
            var top = burning.Take(2).ToList();
            var reason = string.Join(" ", top.Select(x =>
            {
                var u = x.u!;
                var uses = x.ri.Quantity;
                return $"«{u.Name}» (партия {u.DeliveryNumber}) — осталось {Fmt(u.HoursLeft)} ч, на складе {Fmt(u.StockQty)} {u.Unit}; это блюдо использует {Fmt(uses)} {u.Unit}.";
            })) + " Рекомендуем продать до списания.";

            offers.Add(new HotOffer(dish.Id, dish.Name, score, reason));
        }

        return offers.OrderByDescending(o => o.Score).Take(count).ToList();
    }

    private static string Fmt(double v) => (v % 1 == 0 ? v.ToString("0.#", CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture));
    private static string Fmt(decimal v) => (v % 1 == 0 ? v.ToString("0.#", CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture));
}
