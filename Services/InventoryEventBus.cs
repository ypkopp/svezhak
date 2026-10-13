namespace SmartInventory.Services;

/// <summary>
/// Лёгкий канал доменных событий склада. SignalR-хаб подписывается на него,
/// а сервисы/BackgroundService публикуют события без знания о транспорте.
/// </summary>
public class InventoryEventBus
{
    public const string StockChanged = "stock_changed";
    public const string OrderCreated = "order_created";
    public const string BatchExpired = "batch_expired";

    private readonly List<Func<string, int, Task>> _subscribers = [];
    private readonly object _gate = new();

    /// <summary>Подписаться на события. Возвращает IDisposable для отписки.</summary>
    public IDisposable Subscribe(Func<string, int, Task> handler)
    {
        lock (_gate) _subscribers.Add(handler);
        return new Subscription(() => { lock (_gate) _subscribers.Remove(handler); });
    }

    /// <summary>Опубликовать событие для ресторана.</summary>
    public async Task PublishAsync(string eventName, int restaurantId, CancellationToken ct = default)
    {
        Func<string, int, Task>[] handlers;
        lock (_gate) handlers = _subscribers.ToArray();

        foreach (var h in handlers)
        {
            if (ct.IsCancellationRequested) break;
            await h(eventName, restaurantId);
        }
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
