using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartInventory.Data;
using SmartInventory.Models.Inventory;

namespace SmartInventory.Services;

/// <summary>
/// Restaurant-scoped advisor. Deterministic inventory rules are authoritative;
/// an optional OpenAI-compatible provider only turns that snapshot into prose.
/// </summary>
public sealed class AdvisorService(
    ApplicationDbContext db,
    HotOffersService hotOffers,
    OrderService orders,
    IMemoryCache cache,
    IHttpClientFactory clients,
    IConfiguration configuration,
    ILogger<AdvisorService> logger)
{
    public sealed record Insight(string Category, string Title, string Explanation, string Icon, string ActionLabel, string ActionUrl, string Severity);
    public sealed record InsightsResult(int RestaurantId, DateTime GeneratedAtUtc, bool UsedAi, IReadOnlyList<Insight> Insights);
    private sealed record Snapshot(List<Insight> LocalInsights, object Data);

    public async Task<InsightsResult> GetInsightsAsync(int restaurantId, CancellationToken ct = default)
    {
        var key = $"advisor:{restaurantId}";
        if (cache.TryGetValue<InsightsResult>(key, out var cached) && cached is not null)
            return cached;

        var snapshot = await BuildSnapshotAsync(restaurantId, ct);
        IReadOnlyList<Insight> insights = snapshot.LocalInsights;
        var usedAi = false;

        var baseUrl = configuration["Ai:BaseUrl"]?.TrimEnd('/');
        var model = configuration["Ai:Model"];
        var apiKey = configuration["Ai:ApiKey"];
        if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(apiKey))
        {
            try
            {
                var generated = await GenerateWithAiAsync(baseUrl, model, apiKey, snapshot.Data, ct);
                if (generated.Count > 0)
                {
                    insights = MergeEvidence(snapshot.LocalInsights, generated);
                    usedAi = true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Advisor AI provider unavailable; using local rules ({ErrorType})", ex.GetType().Name);
            }
        }

        var result = new InsightsResult(restaurantId, DateTime.UtcNow, usedAi, insights);
        cache.Set(key, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(7),
            SlidingExpiration = TimeSpan.FromMinutes(5)
        });
        return result;
    }

    private async Task<Snapshot> BuildSnapshotAsync(int restaurantId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var from = now.AddDays(-7);
        var liveBatches = await db.Batches
            .AsNoTracking()
            .Include(b => b.Product)
            .Include(b => b.Delivery)
            .Where(b => b.RestaurantId == restaurantId && b.QtyRemaining > 0 && b.ExpiresAt > now)
            .OrderBy(b => b.ExpiresAt)
            .ToListAsync(ct);
        var expiring = liveBatches.Where(b => b.ExpiresAt <= now.AddHours(24)).ToList();
        var expiredWriteOffs = await db.WriteOffs.AsNoTracking()
            .Where(w => w.RestaurantId == restaurantId && w.Reason == OrderService.ReasonExpired && w.CreatedAt >= from)
            .Select(w => new { w.ProductId, w.Qty })
            .ToListAsync(ct);
        var recentOrders = await db.Orders.AsNoTracking()
            .Where(o => o.RestaurantId == restaurantId && o.Status == OrderService.StatusAccepted && o.CreatedAt >= from)
            .Include(o => o.Items)
            .ToListAsync(ct);
        var dishes = await db.Dishes.AsNoTracking()
            .Where(d => d.RestaurantId == restaurantId && d.IsActive)
            .Include(d => d.RecipeItems)
            .ToListAsync(ct);
        var products = await db.Products.AsNoTracking()
            .Where(p => p.RestaurantId == restaurantId)
            .ToDictionaryAsync(p => p.Id, ct);
        var hot = await hotOffers.GetTopAsync(restaurantId, 10, ct);
        var stop = await orders.GetStopListAsync(restaurantId, ct);
        var stopIds = stop.Select(s => s.DishId).ToHashSet();

        var consumption = new Dictionary<int, decimal>();
        var dishById = dishes.ToDictionary(d => d.Id);
        foreach (var order in recentOrders)
        foreach (var item in order.Items)
        {
            if (!dishById.TryGetValue(item.DishId, out var dish)) continue;
            foreach (var recipe in dish.RecipeItems)
                consumption[recipe.ProductId] = consumption.GetValueOrDefault(recipe.ProductId) + recipe.Quantity * item.Qty;
        }
        var remaining = liveBatches.GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.Sum(b => b.QtyRemaining));
        var dailyUse = consumption.ToDictionary(x => x.Key, x => x.Value / 7m);

        var local = new List<Insight>();
        foreach (var h in hot.Take(3))
            local.Add(new Insight("продать срочно", h.DishName, h.Reason, "st-hot.png", "Открыть заказ", "/Inventory/Order", "high"));

        foreach (var s in stop.Take(4))
        {
            var details = string.Join("; ", s.Shortages.Select(x => $"{x.ProductName}: нужно {x.Needed:0.##} {x.Unit}, доступно {x.Available:0.##} {x.Unit}"));
            local.Add(new Insight("заказать", $"Вернуть в меню: {s.DishName}", details, "st-box.png", "Принять поставку", "/Inventory/Deliveries", "high"));
        }

        foreach (var b in expiring.Take(4))
        {
            var hours = Math.Max(0, (b.ExpiresAt - now).TotalHours);
            local.Add(new Insight("списать", $"Проверить партию {b.Delivery?.Number}", $"{b.Product?.Name}: {b.QtyRemaining:0.##} {b.Product?.Unit}, до срока {hours:0.#} ч. Если продукт непригоден — оформите списание.", "st-hourglass.png", "Открыть склад", "/Inventory/Stock?expiringOnly=true", hours < 6 ? "high" : "medium"));
        }

        foreach (var (productId, perDay) in dailyUse.Where(x => x.Value > 0).OrderBy(x => remaining.GetValueOrDefault(x.Key) / x.Value).Take(4))
        {
            var daysLeft = (double)(remaining.GetValueOrDefault(productId) / perDay);
            if (daysLeft > 7) continue;
            var product = products.GetValueOrDefault(productId);
            if (product is null) continue;
            local.Add(new Insight("заказать", $"Запланировать {product.Name}", $"При текущем расходе запаса хватит примерно на {daysLeft:0.#} дн. Остаток: {remaining.GetValueOrDefault(productId):0.##} {product.Unit}.", "st-box.png", "Принять поставку", "/Inventory/Deliveries", daysLeft < 2 ? "high" : "medium"));
        }

        if (local.Count == 0)
            local.Add(new Insight("риски", "Склад под контролем", "Нет срочных рекомендаций. Продолжайте следить за остатками и сроками.", "st-check.png", "Открыть склад", "/Inventory/Stock", "low"));

        var data = new
        {
            generatedAtUtc = now,
            liveBatchCount = liveBatches.Count,
            expiring24h = expiring.Select(b => new { product = b.Product?.Name, unit = b.Product?.Unit, qty = b.QtyRemaining, hoursLeft = Math.Round((b.ExpiresAt - now).TotalHours, 1), delivery = b.Delivery?.Number }).ToList(),
            stopList = stop.Select(s => new { dish = s.DishName, shortages = s.Shortages.Select(x => new { product = x.ProductName, needed = x.Needed, available = x.Available, unit = x.Unit }) }).ToList(),
            hotOffers = hot.Select(h => new { dish = h.DishName, score = h.Score, reason = h.Reason }).ToList(),
            productUsage7d = dailyUse.Select(x => new { product = products.GetValueOrDefault(x.Key)?.Name, stock = remaining.GetValueOrDefault(x.Key), dailyUse = x.Value }).ToList(),
            expiredWriteOffQty7d = expiredWriteOffs.Sum(x => x.Qty),
            acceptedOrders7d = recentOrders.Count,
            localRecommendations = local.Select(x => new { x.Category, x.Title, x.Explanation, x.Severity }).ToList()
        };
        return new Snapshot(local, data);
    }

    private async Task<List<Insight>> GenerateWithAiAsync(string baseUrl, string model, string apiKey, object data, CancellationToken ct)
    {
        var client = clients.CreateClient(nameof(AdvisorService));
        client.Timeout = TimeSpan.FromSeconds(12);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model,
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = "Ты советник ресторанного склада. Используй только переданные факты. Не выдумывай количества и сроки. Верни только JSON-объект {\"insights\":[{\"category\":\"продать срочно|заказать|списать|риски\",\"title\":\"...\",\"explanation\":\"...\",\"severity\":\"low|medium|high\"}]}. Не более 8 инсайтов." },
                new { role = "user", content = JsonSerializer.Serialize(data) }
            }
        }), Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var content = json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content)) return [];
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return [];
        using var payload = JsonDocument.Parse(content[start..(end + 1)]);
        if (!payload.RootElement.TryGetProperty("insights", out var insights) || insights.ValueKind != JsonValueKind.Array) return [];

        var result = new List<Insight>();
        foreach (var item in insights.EnumerateArray().Take(8))
        {
            var category = GetString(item, "category");
            var title = GetString(item, "title");
            var explanation = GetString(item, "explanation");
            var severity = GetString(item, "severity");
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(explanation)) continue;
            var matched = NormalizeCategory(category);
            result.Add(new Insight(matched, title, explanation, IconFor(matched), ActionFor(matched).label, ActionFor(matched).url, severity is "high" or "medium" ? severity : "low"));
        }
        return result;
    }

    private static IReadOnlyList<Insight> MergeEvidence(IReadOnlyList<Insight> local, IReadOnlyList<Insight> ai)
    {
        // Preserve verified local actions/URLs, use AI narrative only for matching category/title.
        var result = new List<Insight>();
        foreach (var insight in ai)
        {
            var evidence = local.FirstOrDefault(x => x.Category == insight.Category);
            result.Add(evidence is null ? insight : insight with { ActionLabel = evidence.ActionLabel, ActionUrl = evidence.ActionUrl, Icon = evidence.Icon });
        }
        return result.Count == 0 ? local : result;
    }

    private static string GetString(JsonElement e, string property) => e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static string NormalizeCategory(string category) => category switch
    {
        "продать срочно" => category,
        "заказать" => category,
        "списать" => category,
        _ => "риски"
    };
    private static string IconFor(string category) => category switch
    {
        "продать срочно" => "st-hot.png",
        "заказать" => "st-box.png",
        "списать" => "st-hourglass.png",
        _ => "st-stop.png"
    };
    private static (string label, string url) ActionFor(string category) => category switch
    {
        "продать срочно" => ("Открыть заказ", "/Inventory/Order"),
        "заказать" => ("Принять поставку", "/Inventory/Deliveries"),
        "списать" => ("Открыть склад", "/Inventory/Stock?expiringOnly=true"),
        _ => ("Открыть склад", "/Inventory/Stock")
    };
}
