using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Models.Inventory;

namespace SmartInventory.Services;

/// <summary>
/// Почасовой авто-анализ: раз в час дёргает AdvisorService.GetSummaryAsync
/// для каждого ресторана и складывает результат в таблицу AiInsight.
/// Дашборд читает последнюю запись из таблицы — LLM не дёргается на каждый просмотр.
/// </summary>
public sealed class AiInsightService(
    IServiceProvider services,
    ILogger<AiInsightService> logger) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Небольшая стартовая задержка — дать сиду и миграциям отработать
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) { logger.LogWarning(ex, "AiInsight tick failed"); }

            await Task.Delay(Period, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var advisor = scope.ServiceProvider.GetRequiredService<AdvisorService>();

        var restaurantIds = await db.Restaurants.Select(r => r.Id).ToListAsync(ct);
        foreach (var rid in restaurantIds)
        {
            try
            {
                var summary = await advisor.GetSummaryAsync(rid, ct);
                db.AiInsights.Add(new AiInsight
                {
                    RestaurantId = rid,
                    Text = summary.Text,
                    ChipsJson = System.Text.Json.JsonSerializer.Serialize(
                        summary.Highlights.Select(h => new { h.Category, h.Title, h.Severity, h.ActionUrl })),
                    UsedAi = summary.UsedAi,
                });
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning("AiInsight for restaurant {Rid} failed: {Err}", rid, ex.GetType().Name);
            }
        }

        // Храним только последние 200 записей на ресторан — не плодим мусор
        var cutoff = DateTime.UtcNow.AddDays(-30);
        await db.AiInsights.Where(a => a.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
