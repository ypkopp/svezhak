using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Services;

namespace Svezhak.Tests;

public sealed class OrderServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SqliteConnection _conn;
    private readonly Fixture _fx;
    private readonly OrderService _svc;

    public OrderServiceTests()
    {
        (_db, _conn) = TestDbFactory.Create();
        _fx = new Fixture(_db);
        _svc = new OrderService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task PlaceOrder_WritesOffFefo_SplitsAcrossBatchesInExpiryOrder()
    {
        // Две партии: ранняя 10 кг (expires +6ч), поздняя 10 кг (expires +72ч)
        var flour = _fx.AddProduct("Мука", "кг");
        var early = _fx.AddBatch(flour, 10m, expiresInHours: 6);
        var late = _fx.AddBatch(flour, 10m, expiresInHours: 72);
        var dish = _fx.AddDish("Лепёшка", 100m, (flour, 2m)); // 2 кг на порцию

        // 6 порций = 12 кг → 10 с ранней + 2 с поздней
        var result = await _svc.PlaceOrderAsync(TestDbFactory.RestaurantId, [(dish.Id, 6)]);

        Assert.Equal("accepted", result.Status);
        Assert.Null(result.RejectReason);

        var writeOffs = await _db.WriteOffs.Where(w => w.OrderId == result.OrderId).ToListAsync();
        Assert.Equal(2, writeOffs.Count);

        var fromEarly = writeOffs.Single(w => w.BatchId == early.Id);
        var fromLate = writeOffs.Single(w => w.BatchId == late.Id);
        Assert.Equal(10m, fromEarly.Qty);
        Assert.Equal(2m, fromLate.Qty);
        Assert.All(writeOffs, w => Assert.Equal("order", w.Reason));

        Assert.Equal(0m, (await _db.Batches.FindAsync(early.Id))!.QtyRemaining);
        Assert.Equal(8m, (await _db.Batches.FindAsync(late.Id))!.QtyRemaining);
    }

    [Fact]
    public async Task PlaceOrder_WhenShortage_RejectsListingAllShortages_AndKeepsStock()
    {
        // Один ингредиент в дефиците, второй — с запасом и третьим вообще без партий
        var salmon = _fx.AddProduct("Лосось", "кг");
        var rice = _fx.AddProduct("Рис", "кг");
        var tobiko = _fx.AddProduct("Тобико", "кг");
        _fx.AddBatch(salmon, 1m, expiresInHours: 48);
        _fx.AddBatch(rice, 100m, expiresInHours: 100);
        var dish = _fx.AddDish("Ролл", 500m, (salmon, 0.4m), (rice, 0.1m), (tobiko, 0.02m));

        var result = await _svc.PlaceOrderAsync(TestDbFactory.RestaurantId, [(dish.Id, 10)]);

        Assert.Equal("rejected", result.Status);
        Assert.NotNull(result.RejectReason);
        // ВСЕ дефицитные позиции: лосось (нужно 4, есть 1) и тобико (нужно 0.2, есть 0)
        Assert.Contains("Лосось", result.RejectReason);
        Assert.Contains("Тобико", result.RejectReason);
        Assert.DoesNotContain("Рис", result.RejectReason);

        // Остатки не изменились, WriteOff не создан
        Assert.Equal(1m, (await _db.Batches.SingleAsync(b => b.ProductId == salmon.Id)).QtyRemaining);
        Assert.Equal(100m, (await _db.Batches.SingleAsync(b => b.ProductId == rice.Id)).QtyRemaining);
        Assert.Empty(await _db.WriteOffs.ToListAsync());
    }

    [Fact]
    public async Task PlaceOrder_ExpiredBatch_DoesNotCountAsAvailable()
    {
        var milk = _fx.AddProduct("Молоко", "л");
        _fx.AddBatch(milk, 50m, expiresInHours: -2); // просрочена 2 часа назад
        var dish = _fx.AddDish("Каша", 200m, (milk, 0.3m));

        var result = await _svc.PlaceOrderAsync(TestDbFactory.RestaurantId, [(dish.Id, 1)]);

        Assert.Equal("rejected", result.Status);
        Assert.Contains("Молоко", result.RejectReason);
        Assert.Contains("доступно 0", result.RejectReason);

        // Физический остаток на месте, списаний нет
        Assert.Equal(50m, (await _db.Batches.SingleAsync()).QtyRemaining);
        Assert.Empty(await _db.WriteOffs.ToListAsync());
    }

    [Fact]
    public async Task PlaceOrder_OverOrder_NeverDrivesRemainingBelowZero()
    {
        var cheese = _fx.AddProduct("Сыр", "кг");
        var b1 = _fx.AddBatch(cheese, 2m, expiresInHours: 48);
        var b2 = _fx.AddBatch(cheese, 3m, expiresInHours: 72);
        var dish = _fx.AddDish("Пицца", 600m, (cheese, 0.5m));

        // Доступно 5 кг, просим 6 кг → rejected
        var result = await _svc.PlaceOrderAsync(TestDbFactory.RestaurantId, [(dish.Id, 12)]);

        Assert.Equal("rejected", result.Status);
        var batches = await _db.Batches.ToListAsync();
        Assert.All(batches, b => Assert.True(b.QtyRemaining >= 0));
        Assert.Equal(2m, batches.Single(b => b.Id == b1.Id).QtyRemaining);
        Assert.Equal(3m, batches.Single(b => b.Id == b2.Id).QtyRemaining);
    }

    [Fact]
    public async Task StopList_AfterIngredientRunsOut_ContainsDishWithShortage()
    {
        var avocado = _fx.AddProduct("Авокадо", "шт");
        var rice = _fx.AddProduct("Рис", "кг");
        _fx.AddBatch(avocado, 2m, expiresInHours: 48);
        _fx.AddBatch(rice, 100m, expiresInHours: 100);
        var roll = _fx.AddDish("Ролл", 500m, (avocado, 1m), (rice, 0.1m));
        var riceOnly = _fx.AddDish("Рис отварной", 150m, (rice, 0.2m));

        // Исходно стоп-лист пуст
        Assert.Empty(await _svc.GetStopListAsync(TestDbFactory.RestaurantId));

        // Исчерпываем авокадо двумя заказами
        Assert.Equal("accepted", (await _svc.PlaceOrderAsync(TestDbFactory.RestaurantId, [(roll.Id, 1)])).Status);
        Assert.Equal("accepted", (await _svc.PlaceOrderAsync(TestDbFactory.RestaurantId, [(roll.Id, 1)])).Status);

        var stopList = await _svc.GetStopListAsync(TestDbFactory.RestaurantId);
        var item = Assert.Single(stopList);
        Assert.Equal(roll.Id, item.DishId);
        Assert.Equal("Ролл", item.DishName);
        var shortage = Assert.Single(item.Shortages);
        Assert.Equal("Авокадо", shortage.ProductName);
        Assert.Equal(1m, shortage.Needed);
        Assert.Equal(0m, shortage.Available);

        // Блюдо без дефицита в стоп-листе отсутствует
        Assert.DoesNotContain(stopList, s => s.DishId == riceOnly.Id);
    }
}
