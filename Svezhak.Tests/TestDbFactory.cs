using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SmartInventory.Data;
using SmartInventory.Models.Inventory;

namespace Svezhak.Tests;

/// <summary>
/// Фабрика изолированного контекста: каждая БД живёт на открытом
/// Sqlite in-memory соединении и уничтожается вместе с ним.
/// </summary>
public static class TestDbFactory
{
    public const int RestaurantId = 1;

    public static (ApplicationDbContext Db, SqliteConnection Conn) Create()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(conn)
            .Options;

        var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();
        return (db, conn);
    }
}

/// <summary>
/// Билдер минимальной предметной области для тестов OrderService.
/// </summary>
public sealed class Fixture
{
    public ApplicationDbContext Db { get; }
    private readonly List<Product> _products = [];
    private readonly List<Dish> _dishes = [];
    private int _deliveryNo;

    public Fixture(ApplicationDbContext db) => Db = db;

    public Product AddProduct(string name, string unit)
    {
        var p = new Product { RestaurantId = TestDbFactory.RestaurantId, Name = name, Unit = unit };
        Db.Products.Add(p);
        Db.SaveChanges();
        _products.Add(p);
        return p;
    }

    public Dish AddDish(string name, decimal price, params (Product Product, decimal Qty)[] recipe)
    {
        var d = new Dish { RestaurantId = TestDbFactory.RestaurantId, Name = name, Price = price };
        foreach (var (product, qty) in recipe)
        {
            d.RecipeItems.Add(new RecipeItem { Dish = d, ProductId = product.Id, Quantity = qty });
        }
        Db.Dishes.Add(d);
        Db.SaveChanges();
        _dishes.Add(d);
        return d;
    }

    public Batch AddBatch(Product product, decimal qty, double expiresInHours)
    {
        var delivery = new Delivery
        {
            RestaurantId = TestDbFactory.RestaurantId,
            Number = $"T-{++_deliveryNo:000}",
            CreatedAt = DateTime.UtcNow,
        };
        var batch = new Batch
        {
            RestaurantId = TestDbFactory.RestaurantId,
            Delivery = delivery,
            ProductId = product.Id,
            QtyInitial = qty,
            QtyRemaining = qty,
            ReceivedAt = delivery.CreatedAt,
            ExpiresAt = DateTime.UtcNow.AddHours(expiresInHours),
        };
        Db.Deliveries.Add(delivery);
        Db.Batches.Add(batch);
        Db.SaveChanges();
        return batch;
    }
}
