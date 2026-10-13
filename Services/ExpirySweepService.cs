using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Models.Inventory;

namespace SmartInventory.Services;

/// <summary>
/// Фоновая задача: раз в 60 сек списывает просроченные партии
/// (ExpiresAt <= now и QtyRemaining > 0) → WriteOff(reason=expired) и обнуление остатка.
/// Одна транзакция на весь пакет.
/// </summary>
public class ExpirySweepService(
    IServiceProvider services,
    ILogger<ExpirySweepService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ExpirySweepService запущен — свип просрочки каждые {Sec} сек", Interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ошибка при свипе просроченных партий");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger.LogInformation("ExpirySweepService остановлен");
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var events = scope.ServiceProvider.GetService<InventoryEventBus>();

        var now = DateTime.UtcNow;

        // Партии, у которых истёк срок, но остаток ещё есть
        var expired = await db.Batches
            .Include(b => b.Product)
            .Where(b => b.QtyRemaining > 0 && b.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var batch in expired)
        {
            var qty = batch.QtyRemaining;
            batch.QtyRemaining = 0;

            db.WriteOffs.Add(new WriteOff
            {
                RestaurantId = batch.RestaurantId,
                BatchId = batch.Id,
                ProductId = batch.ProductId,
                Qty = qty,
                Reason = OrderService.ReasonExpired,
                OrderId = null,
                CreatedAt = now,
            });

            logger.LogInformation(
                "[SWEEP] Партия #{BatchId} ({Product}) просрочена — списано {Qty} {Unit} (delivery остаток обнулён)",
                batch.Id, batch.Product?.Name ?? $"#{batch.ProductId}", qty, batch.Product?.Unit ?? "");
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        logger.LogInformation("Свип просрочки: списано {Count} партий", expired.Count);

        // Уведомляем подписчиков (SignalR) об изменении склада
        if (events is not null)
        {
            var restaurantIds = expired.Select(b => b.RestaurantId).Distinct();
            foreach (var rid in restaurantIds)
            {
                await events.PublishAsync(InventoryEventBus.BatchExpired, rid, ct);
                await events.PublishAsync(InventoryEventBus.StockChanged, rid, ct);
            }
        }
    }
}
