using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Services;
using System.Security.Claims;

namespace SmartInventory.Hubs;

/// <summary>
/// SignalR-хаб склада: /hubs/inventory.
/// Рассылает события stock_changed / order_created / batch_expired
/// только клиентам того же ресторана.
/// </summary>
public class InventoryHub(
    ApplicationDbContext db,
    InventoryEventBus bus,
    ILogger<InventoryHub> logger) : Hub
{
    private IDisposable? _subscription;
    private int _restaurantId;

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var codeIdStr = user.FindFirstValue("AccessCodeId") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(codeIdStr, out var codeId))
            {
                var orgId = await db.AccessCodes
                    .Where(c => c.Id == codeId)
                    .Select(c => (int?)c.OrganizationId)
                    .FirstOrDefaultAsync();
                var rid = orgId is null ? null : await db.Restaurants
                    .Where(r => r.OrganizationId == orgId)
                    .Select(r => (int?)r.Id)
                    .FirstOrDefaultAsync();

                if (rid is not null)
                {
                    _restaurantId = rid.Value;
                    var connId = Context.ConnectionId;

                    _subscription = bus.Subscribe(async (eventName, eventRid) =>
                    {
                        if (eventRid != _restaurantId) return;
                        try
                        {
                            await Clients.Client(connId).SendAsync(eventName, eventRid);
                        }
                        catch { /* клиент отключился */ }
                    });

                    logger.LogInformation("SignalR подключён {Conn} → ресторан {Rid}", connId, _restaurantId);
                }
            }
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _subscription?.Dispose();
        await base.OnDisconnectedAsync(exception);
    }
}
