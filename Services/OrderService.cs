using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Models.Inventory;

namespace SmartInventory.Services;

/// <summary>
/// Ядро доменной логики склада: приём заказов со списанием по FEFO
/// и вычисляемый стоп-лист блюд.
/// </summary>
public class OrderService(ApplicationDbContext db)
{
    public const string StatusAccepted = "accepted";
    public const string StatusRejected = "rejected";
    public const string ReasonOrder = "order";
    public const string ReasonExpired = "expired";

    /// <summary>Дефицитная позиция: сколько продукта нужно и сколько реально доступно.</summary>
    public record Shortage(int ProductId, string ProductName, string Unit, decimal Needed, decimal Available)
    {
        public override string ToString() =>
            $"{ProductName}: нужно {Fmt(Needed)} {Unit}, доступно {Fmt(Available)} {Unit}";
    }

    /// <summary>Блюдо, попавшее в стоп-лист, с детализацией дефицита.
    /// IsManual=true — блюдо остановлено вручную (ManualStopped), Comment — причина.</summary>
    public record StopListItem(int DishId, string DishName, List<Shortage> Shortages, bool IsManual = false, string? Comment = null);

    /// <summary>Итог обработки заказа.</summary>
    public record OrderResult(int OrderId, string Status, string? RejectReason);

    /// <summary>
    /// Принимает заказ: разворачивает блюда в потребность в продуктах,
    /// проверяет доступность по живым партиям и списывает по FEFO.
    /// Вся операция — в одной транзакции БД.
    /// </summary>
    /// <param name="items">Позиции заказа: (DishId, количество порций).</param>
    public async Task<OrderResult> PlaceOrderAsync(
        int restaurantId,
        IReadOnlyCollection<(int DishId, int Qty)> items,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
            throw new ArgumentException("Заказ не может быть пустым.", nameof(items));
        if (items.Any(i => i.Qty <= 0))
            throw new ArgumentException("Количество порций должно быть положительным.", nameof(items));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTime.UtcNow;

        var order = new Order
        {
            RestaurantId = restaurantId,
            CreatedAt = now,
            Status = StatusAccepted,
            Items = items
                .GroupBy(i => i.DishId)
                .Select(g => new OrderItem { DishId = g.Key, Qty = g.Sum(i => i.Qty) })
                .ToList(),
        };
        db.Orders.Add(order);

        // 1. Разворачиваем заказ в потребность: Σ RecipeItem.Quantity × OrderItem.Qty по каждому продукту
        var dishIds = order.Items.Select(i => i.DishId).ToList();
        var dishes = await db.Dishes
            .Where(d => d.RestaurantId == restaurantId && dishIds.Contains(d.Id))
            .Include(d => d.RecipeItems)
            .ToListAsync(ct);

        var shortages = new List<Shortage>();

        // Блюда, которых нет или которые сняты с продажи — тоже дефицит
        var manualStopReasons = new List<string>();
        foreach (var item in order.Items)
        {
            var dish = dishes.FirstOrDefault(d => d.Id == item.DishId);
            if (dish is null)
            {
                shortages.Add(new Shortage(item.DishId, $"Блюдо #{item.DishId}", "шт", item.Qty, 0));
            }
            else if (!dish.IsActive)
            {
                shortages.Add(new Shortage(item.DishId, $"{dish.Name} (снято с продажи)", "шт", item.Qty, 0));
            }
            else if (dish.ManualStopped)
            {
                // Ручной стоп — отдельной строкой, с причиной если задана
                var reason = string.IsNullOrWhiteSpace(dish.StopComment)
                    ? $"«{dish.Name}» остановлено вручную"
                    : $"«{dish.Name}» остановлено вручную: {dish.StopComment}";
                manualStopReasons.Add(reason);
            }
        }

        var needed = new Dictionary<int, decimal>();
        foreach (var item in order.Items)
        {
            var dish = dishes.FirstOrDefault(d => d.Id == item.DishId);
            if (dish is null || !dish.IsActive || dish.ManualStopped) continue;

            foreach (var ri in dish.RecipeItems)
            {
                needed[ri.ProductId] = needed.GetValueOrDefault(ri.ProductId) + ri.Quantity * item.Qty;
            }
        }

        // 2. Живые партии нужных продуктов: просроченные (ExpiresAt <= now) не считаются
        var productIds = needed.Keys.ToList();
        var batches = await db.Batches
            .Where(b => b.RestaurantId == restaurantId
                        && productIds.Contains(b.ProductId)
                        && b.QtyRemaining > 0
                        && b.ExpiresAt > now)
            .OrderBy(b => b.ExpiresAt)
            .ToListAsync(ct);

        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        // 3. Проверяем доступность и собираем ВСЕ дефицитные позиции
        var batchesByProduct = batches.GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (productId, qtyNeeded) in needed)
        {
            var available = batchesByProduct.GetValueOrDefault(productId)?.Sum(b => b.QtyRemaining) ?? 0m;
            if (available < qtyNeeded)
            {
                var product = products.GetValueOrDefault(productId);
                shortages.Add(new Shortage(
                    productId,
                    product?.Name ?? $"Продукт #{productId}",
                    product?.Unit ?? "ед.",
                    qtyNeeded,
                    available));
            }
        }

        if (shortages.Count > 0 || manualStopReasons.Count > 0)
        {
            order.Status = StatusRejected;
            var parts = manualStopReasons.Concat(shortages.Select(s => s.ToString()));
            order.RejectReason = string.Join("; ", parts);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new OrderResult(order.Id, order.Status, order.RejectReason);
        }

        // 4. FEFO-списание: сначала план (какие партии и сколько), потом применение —
        //    так QtyRemaining никогда не уйдёт в минус даже при конкурентном изменении.
        var writeOffs = new List<WriteOff>();
        var plan = new List<(Batch Batch, decimal Take)>();

        foreach (var (productId, qtyNeeded) in needed)
        {
            var rest = qtyNeeded;
            foreach (var batch in batchesByProduct[productId])
            {
                if (rest <= 0) break;
                var take = Math.Min(batch.QtyRemaining, rest);
                if (take <= 0) continue;
                plan.Add((batch, take));
                rest -= take;
            }

            if (rest > 0)
            {
                // Остатки изменились между проверкой и списанием — не даём уйти в минус
                throw new InvalidOperationException(
                    $"Недостаточно остатков продукта #{productId}: остаток изменился во время оформления заказа.");
            }
        }

        foreach (var (batch, take) in plan)
        {
            batch.QtyRemaining -= take;
            writeOffs.Add(new WriteOff
            {
                RestaurantId = restaurantId,
                BatchId = batch.Id,
                ProductId = batch.ProductId,
                Qty = take,
                Reason = ReasonOrder,
                Order = order,
                CreatedAt = now,
            });
        }

        db.WriteOffs.AddRange(writeOffs);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new OrderResult(order.Id, order.Status, null);
    }

    /// <summary>
    /// Вычисляемый стоп-лист (не таблица): активные блюда, для которых
    /// не хватает хотя бы одного ингредиента даже на 1 порцию.
    /// </summary>
    public async Task<List<StopListItem>> GetStopListAsync(int restaurantId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var dishes = await db.Dishes
            .Where(d => d.RestaurantId == restaurantId && d.IsActive)
            .Include(d => d.RecipeItems)
            .ToListAsync(ct);

        var productIds = dishes.SelectMany(d => d.RecipeItems).Select(r => r.ProductId).Distinct().ToList();

        var products = await db.Products
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        // Доступный остаток = Σ QtyRemaining по живым партиям
        var availability = await db.Batches
            .Where(b => b.RestaurantId == restaurantId
                        && productIds.Contains(b.ProductId)
                        && b.QtyRemaining > 0
                        && b.ExpiresAt > now)
            .GroupBy(b => b.ProductId)
            .Select(g => new { ProductId = g.Key, Available = g.Sum(b => b.QtyRemaining) })
            .ToDictionaryAsync(x => x.ProductId, x => x.Available, ct);

        var stopList = new List<StopListItem>();
        foreach (var dish in dishes)
        {
            // Ручной стоп — блюдо в стоп-листе без проверки ингредиентов
            if (dish.ManualStopped)
            {
                stopList.Add(new StopListItem(dish.Id, dish.Name, [], IsManual: true, Comment: dish.StopComment));
                continue;
            }

            var shortages = dish.RecipeItems
                .Select(ri =>
                {
                    var available = availability.GetValueOrDefault(ri.ProductId);
                    var product = products.GetValueOrDefault(ri.ProductId);
                    return (ri, available, product);
                })
                .Where(x => x.available < x.ri.Quantity)
                .Select(x => new Shortage(
                    x.ri.ProductId,
                    x.product?.Name ?? $"Продукт #{x.ri.ProductId}",
                    x.product?.Unit ?? "ед.",
                    x.ri.Quantity,
                    x.available))
                .ToList();

            if (shortages.Count > 0)
            {
                stopList.Add(new StopListItem(dish.Id, dish.Name, shortages));
            }
        }

        return stopList;
    }

    private static string Fmt(decimal v) =>
        (v % 1 == 0 ? v.ToString("0") : v.ToString("0.00"))
        .Replace(',', '.');
}
