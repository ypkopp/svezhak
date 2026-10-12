using Microsoft.EntityFrameworkCore;
using SmartInventory.Authorization;
using SmartInventory.Models.Inventory;
using SmartInventory.Models.Structure;

namespace SmartInventory.Data;

/// <summary>
/// Наполняет пустую БД демо-данными: организация + ресторан,
/// продукты, блюда с рецептами, три поставки с разными сроками годности
/// и один активный AccessCode менеджера для входа.
/// </summary>
public static class DbInitializer
{
    public const string ManagerAccessCode = "MANAGER1234";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();

        await db.Database.MigrateAsync();

        // Сидим только пустую БД: если склад уже наполнен — ничего не трогаем
        if (await db.Products.AnyAsync())
            return;

        var now = DateTime.UtcNow;

        // === Организация и ресторан ===
        var org = await db.Organizations.FirstOrDefaultAsync();
        if (org is null)
        {
            org = new Organization { Name = "Демо-сеть", OwnerId = "pending" };
            db.Organizations.Add(org);
            await db.SaveChangesAsync();
        }

        var restaurant = await db.Restaurants
            .Where(r => r.OrganizationId == org.Id)
            .FirstOrDefaultAsync();
        if (restaurant is null)
        {
            restaurant = new Restaurant
            {
                Address = "ул. Демонстрационная, 1",
                OrganizationId = org.Id,
                ManagerId = "pending",
            };
            db.Restaurants.Add(restaurant);
            await db.SaveChangesAsync();
        }

        // === AccessCode менеджера для входа в демо ===
        if (!await db.AccessCodes.AnyAsync(c => c.Code == ManagerAccessCode))
        {
            var code = new AccessCode(ManagerAccessCode, AppRole.RestaurantManager, org.Id)
            {
                EmployeeName = "Демо-менеджер",
                IsActive = true,
            };
            db.AccessCodes.Add(code);
            await db.SaveChangesAsync();

            // Привязываем демо-ресторан к этому менеджеру,
            // чтобы после входа сразу открывался его дашборд
            if (restaurant.ManagerId == "pending")
            {
                restaurant.ManagerId = code.Id.ToString();
            }
            if (org.OwnerId == "pending")
            {
                org.OwnerId = code.Id.ToString();
            }
            await db.SaveChangesAsync();
        }

        var rid = restaurant.Id;

        // === Продукты (18) ===
        Product P(string name, string unit) => new() { RestaurantId = rid, Name = name, Unit = unit };

        var products = new Dictionary<string, Product>
        {
            ["salmon"] = P("Лосось филе", "кг"),
            ["cream_cheese"] = P("Крем-чиз", "кг"),
            ["rice"] = P("Рис для суши", "кг"),
            ["nori"] = P("Нори", "шт"),
            ["avocado"] = P("Авокадо", "шт"),
            ["chicken"] = P("Куриное филе", "кг"),
            ["beef"] = P("Говядина", "кг"),
            ["tomato"] = P("Томаты", "кг"),
            ["mozzarella"] = P("Моцарелла", "кг"),
            ["dough"] = P("Тесто для пиццы", "кг"),
            ["lettuce"] = P("Салат листовой", "кг"),
            ["cucumber"] = P("Огурцы", "кг"),
            ["shrimp"] = P("Креветки", "кг"),
            ["eel"] = P("Угорь", "кг"),
            ["tobiko"] = P("Икра тобико", "кг"),
            ["soy_sauce"] = P("Соевый соус", "л"),
            ["ginger"] = P("Имбирь маринованный", "кг"),
            ["wasabi"] = P("Васаби", "кг"),
        };
        db.Products.AddRange(products.Values);
        await db.SaveChangesAsync();

        // === Блюда (10) с рецептами: норма расхода на 1 порцию ===
        Dish D(string name, decimal price, params (string Key, decimal Qty)[] recipe)
        {
            var dish = new Dish { RestaurantId = rid, Name = name, Price = price, IsActive = true };
            dish.RecipeItems.AddRange(recipe.Select(r => new RecipeItem
            {
                Dish = dish,
                ProductId = products[r.Key].Id,
                Quantity = r.Qty,
            }));
            return dish;
        }

        var dishes = new[]
        {
            // Роллы (порция = 8 кусков)
            D("Ролл Филадельфия", 550m,
                ("salmon", 0.12m), ("rice", 0.11m), ("nori", 0.5m),
                ("cream_cheese", 0.05m), ("cucumber", 0.04m)),
            D("Ролл Калифорния", 480m,
                ("shrimp", 0.08m), ("avocado", 0.5m), ("rice", 0.10m),
                ("nori", 0.5m), ("tobiko", 0.02m)),
            D("Ролл Дракон", 690m,
                ("eel", 0.05m), ("avocado", 0.5m), ("rice", 0.11m),
                ("nori", 0.5m), ("soy_sauce", 0.01m), ("tobiko", 0.01m)),
            D("Ролл Спайси лосось", 520m,
                ("salmon", 0.10m), ("rice", 0.11m), ("nori", 0.5m),
                ("tobiko", 0.015m), ("soy_sauce", 0.005m)),
            D("Ролл Креветка темпура", 570m,
                ("shrimp", 0.09m), ("cream_cheese", 0.05m), ("rice", 0.11m),
                ("nori", 0.5m), ("cucumber", 0.03m)),
            // Салаты
            D("Салат Цезарь с курицей", 450m,
                ("chicken", 0.12m), ("lettuce", 0.08m), ("tomato", 0.06m),
                ("mozzarella", 0.03m)),
            D("Салат Греческий", 420m,
                ("tomato", 0.15m), ("cucumber", 0.12m), ("mozzarella", 0.08m),
                ("lettuce", 0.03m)),
            // Пицца
            D("Пицца Маргарита", 600m,
                ("dough", 0.25m), ("tomato", 0.08m), ("mozzarella", 0.12m)),
            // Паста
            D("Паста болоньезе", 520m,
                ("dough", 0.12m), ("beef", 0.12m), ("tomato", 0.12m)),
            // Боул
            D("Поке с лососем", 650m,
                ("salmon", 0.10m), ("rice", 0.15m), ("avocado", 0.5m),
                ("cucumber", 0.05m), ("tobiko", 0.02m), ("soy_sauce", 0.02m)),
        };
        db.Dishes.AddRange(dishes);
        await db.SaveChangesAsync();

        // === Поставки и партии ===
        Batch B(Delivery delivery, string key, decimal qty, double expiresInHours)
        {
            var product = products[key];
            return new Batch
            {
                RestaurantId = rid,
                Delivery = delivery,
                ProductId = product.Id,
                QtyInitial = qty,
                QtyRemaining = qty,
                ReceivedAt = delivery.CreatedAt,
                ExpiresAt = now.AddHours(expiresInHours),
            };
        }

        // 1) Свежая поставка: срок годности 72–120 ч — основной запас
        var freshDelivery = new Delivery
        {
            RestaurantId = rid,
            Number = "D-001",
            CreatedAt = now.AddHours(-1),
        };
        db.Deliveries.Add(freshDelivery);
        db.Batches.AddRange(
            B(freshDelivery, "salmon", 5m, 80),
            B(freshDelivery, "cream_cheese", 3m, 100),
            B(freshDelivery, "rice", 20m, 120),
            B(freshDelivery, "nori", 100m, 120),
            B(freshDelivery, "avocado", 20m, 72),
            B(freshDelivery, "chicken", 8m, 76),
            B(freshDelivery, "beef", 8m, 90),
            B(freshDelivery, "tomato", 10m, 80),
            B(freshDelivery, "mozzarella", 6m, 96),
            B(freshDelivery, "dough", 10m, 100),
            B(freshDelivery, "lettuce", 3m, 72),
            B(freshDelivery, "cucumber", 8m, 84),
            B(freshDelivery, "shrimp", 5m, 88),
            B(freshDelivery, "eel", 2m, 92),
            B(freshDelivery, "tobiko", 1m, 110),
            B(freshDelivery, "soy_sauce", 5m, 120),
            B(freshDelivery, "ginger", 1m, 118),
            B(freshDelivery, "wasabi", 0.5m, 115));

        // 2) Скоропортящаяся поставка: срок 6–12 ч — источник Hot Offers
        var hotDelivery = new Delivery
        {
            RestaurantId = rid,
            Number = "D-002",
            CreatedAt = now.AddHours(-30),
        };
        db.Deliveries.Add(hotDelivery);
        db.Batches.AddRange(
            B(hotDelivery, "salmon", 2m, 8),
            B(hotDelivery, "avocado", 10m, 6),
            B(hotDelivery, "lettuce", 1m, 10),
            B(hotDelivery, "shrimp", 1.5m, 12),
            B(hotDelivery, "cream_cheese", 1m, 9));

        // 3) Частично просроченная поставка: часть партий уже мертва
        var staleDelivery = new Delivery
        {
            RestaurantId = rid,
            Number = "D-003",
            CreatedAt = now.AddDays(-3),
        };
        db.Deliveries.Add(staleDelivery);
        db.Batches.AddRange(
            B(staleDelivery, "tomato", 5m, -4),        // просрочено
            B(staleDelivery, "cucumber", 4m, -10),     // просрочено
            B(staleDelivery, "lettuce", 2m, -20),      // просрочено
            B(staleDelivery, "mozzarella", 3m, 20),    // ещё живое
            B(staleDelivery, "dough", 3m, 30));        // ещё живое

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Полный сброс демо: удаляет все данные склада и организации,
    /// затем заново вызывает SeedAsync. Используется кнопкой «Сбросить демо».
    /// </summary>
    public static async Task ResetAndReseedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        // Порядок удаления — от дочерних к родительским (FK = Restrict)
        db.WriteOffs.RemoveRange(db.WriteOffs);
        db.OrderItems.RemoveRange(db.OrderItems);
        db.Orders.RemoveRange(db.Orders);
        db.RecipeItems.RemoveRange(db.RecipeItems);
        db.Batches.RemoveRange(db.Batches);
        db.Deliveries.RemoveRange(db.Deliveries);
        db.Dishes.RemoveRange(db.Dishes);
        db.Products.RemoveRange(db.Products);
        await db.SaveChangesAsync();

        db.AccessCodes.RemoveRange(db.AccessCodes);
        db.Restaurants.RemoveRange(db.Restaurants);
        db.Organizations.RemoveRange(db.Organizations);
        await db.SaveChangesAsync();

        await SeedAsync(services);
    }
}
