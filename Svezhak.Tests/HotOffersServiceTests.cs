using Microsoft.Data.Sqlite;
using SmartInventory.Data;
using SmartInventory.Services;

namespace Svezhak.Tests;

public sealed class HotOffersServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SqliteConnection _conn;
    private readonly Fixture _fx;
    private readonly HotOffersService _hot;
    private readonly OrderService _orders;

    public HotOffersServiceTests()
    {
        (_db, _conn) = TestDbFactory.Create();
        _fx = new Fixture(_db);
        _orders = new OrderService(_db);
        _hot = new HotOffersService(_db, _orders);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
    }

    [Fact]
    public async Task GetTop_DishWithUrgentIngredient_ScoresAboveFreshDish()
    {
        // Продукт А горит через 5 ч, продукт Б — через 96 ч
        var urgent = _fx.AddProduct("Горящий", "кг");
        var fresh = _fx.AddProduct("Свежий", "кг");
        _fx.AddBatch(urgent, 10m, expiresInHours: 5);
        _fx.AddBatch(fresh, 10m, expiresInHours: 96);

        // Блюдо из горящего ингредиента
        var hotDish = _fx.AddDish("Горящее блюдо", 500m, (urgent, 0.5m));
        // Блюдо из свежего
        var freshDish = _fx.AddDish("Свежее блюдо", 500m, (fresh, 0.5m));

        var top = await _hot.GetTopAsync(TestDbFactory.RestaurantId, 10);

        Assert.NotEmpty(top);
        Assert.Equal(hotDish.Id, top[0].DishId);
        Assert.Equal("Горящее блюдо", top[0].DishName);
        Assert.True(top[0].Score > 0);

        // Блюдо со свежим ингредиентом либо ниже по скору, либо вообще не в топе
        var freshEntry = top.FirstOrDefault(x => x.DishId == freshDish.Id);
        if (freshEntry is not null)
        {
            Assert.True(top[0].Score > freshEntry.Score);
        }
    }

    [Fact]
    public async Task GetTop_ExcludesStopListedDishes()
    {
        // Продукт: одна горящая партия + нет остатка для второго блюда
        var p = _fx.AddProduct("Дефицитный", "кг");
        _fx.AddBatch(p, 0.4m, expiresInHours: 5); // хватает только на 0.4 кг

        var hotDish = _fx.AddDish("В стопе", 500m, (p, 1m));   // нужно 1 кг → в стоп-листе
        var okDish = _fx.AddDish("Доступное", 500m, (p, 0.1m)); // 0.1 кг → не в стопе, и горит

        var top = await _hot.GetTopAsync(TestDbFactory.RestaurantId, 10);

        Assert.DoesNotContain(top, x => x.DishId == hotDish.Id);
        Assert.Contains(top, x => x.DishId == okDish.Id);
    }

    [Fact]
    public async Task GetTop_ExpiredBatchesAreIgnored()
    {
        var p = _fx.AddProduct("Просроченный", "кг");
        _fx.AddBatch(p, 10m, expiresInHours: -2); // просрочена — не считается

        var dish = _fx.AddDish("Блюдо", 500m, (p, 0.5m));

        var top = await _hot.GetTopAsync(TestDbFactory.RestaurantId, 10);

        // Нет живых партий → нет горящих ингредиентов → блюдо не в hot offers
        Assert.DoesNotContain(top, x => x.DishId == dish.Id);
    }

    [Fact]
    public async Task GetTop_ReasonMentionsBurningIngredient()
    {
        var p = _fx.AddProduct("Крем-чиз", "кг");
        _fx.AddBatch(p, 2m, expiresInHours: 6);

        var dish = _fx.AddDish("Ролл", 500m, (p, 0.15m));

        var top = await _hot.GetTopAsync(TestDbFactory.RestaurantId, 10);

        Assert.NotEmpty(top);
        Assert.Contains("Крем-чиз", top[0].Reason);
        Assert.Contains("Рекомендуем продать", top[0].Reason);
    }
}
