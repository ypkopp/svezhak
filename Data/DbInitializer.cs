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
    public const string AdminAccessCode = "ADMIN1234";

    public static async Task SeedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();

        // Схему обновляем под провайдер: SQL Server — миграциями,
        // Postgres — EnsureCreated (Npgsql-дифф EF10 падает на enum/DateOnly).
        if (db.Database.IsNpgsql())
            await db.Database.EnsureCreatedAsync();
        else
            await db.Database.MigrateAsync();

        // Сотрудники и доступы сидим идемпотентно — независимо от того,
        // наполнен ли уже склад. Это отдельный блок, чтобы демо-учётки
        // появлялись и на уже засиденной БД.
        await SeedStaffAsync(db);
        // Категории и закупочные цены добиваем тоже идемпотентно.
        await BackfillProductMetaAsync(db);

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
        Product P(string name, string unit, string category, decimal price) =>
            new() { RestaurantId = rid, Name = name, Unit = unit, Category = category, PurchasePrice = price };

        var products = new Dictionary<string, Product>
        {
            ["salmon"] = P("Лосось филе", "кг", "Рыба", 1450m),
            ["cream_cheese"] = P("Крем-чиз", "кг", "Молочное", 780m),
            ["rice"] = P("Рис для суши", "кг", "Бакалея", 160m),
            ["nori"] = P("Нори", "шт", "Сухие", 18m),
            ["avocado"] = P("Авокадо", "шт", "Овощи", 95m),
            ["chicken"] = P("Куриное филе", "кг", "Мясо", 320m),
            ["beef"] = P("Говядина", "кг", "Мясо", 780m),
            ["tomato"] = P("Томаты", "кг", "Овощи", 220m),
            ["mozzarella"] = P("Моцарелла", "кг", "Молочное", 720m),
            ["dough"] = P("Тесто для пиццы", "кг", "Бакалея", 210m),
            ["lettuce"] = P("Салат листовой", "кг", "Овощи", 380m),
            ["cucumber"] = P("Огурцы", "кг", "Овощи", 180m),
            ["shrimp"] = P("Креветки", "кг", "Рыба", 980m),
            ["eel"] = P("Угорь", "кг", "Рыба", 2100m),
            ["tobiko"] = P("Икра тобико", "кг", "Рыба", 3400m),
            ["soy_sauce"] = P("Соевый соус", "л", "Соусы", 420m),
            ["ginger"] = P("Имбирь маринованный", "кг", "Соусы", 460m),
            ["wasabi"] = P("Васаби", "кг", "Соусы", 1600m),
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

    /// <summary>Категория и закупочная цена продукта по имени — для добивки существующих БД.</summary>
    private static readonly Dictionary<string, (string Category, decimal Price)> ProductMeta =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Лосось филе"] = ("Рыба", 1450m),
            ["Креветки"] = ("Рыба", 980m),
            ["Угорь"] = ("Рыба", 2100m),
            ["Икра тобико"] = ("Рыба", 3400m),
            ["Куриное филе"] = ("Мясо", 320m),
            ["Говядина"] = ("Мясо", 780m),
            ["Авокадо"] = ("Овощи", 95m),
            ["Томаты"] = ("Овощи", 220m),
            ["Салат листовой"] = ("Овощи", 380m),
            ["Огурцы"] = ("Овощи", 180m),
            ["Крем-чиз"] = ("Молочное", 780m),
            ["Моцарелла"] = ("Молочное", 720m),
            ["Рис для суши"] = ("Бакалея", 160m),
            ["Тесто для пиццы"] = ("Бакалея", 210m),
            ["Нори"] = ("Сухие", 18m),
            ["Соевый соус"] = ("Соусы", 420m),
            ["Имбирь маринованный"] = ("Соусы", 460m),
            ["Васаби"] = ("Соусы", 1600m),
        };

    /// <summary>Добивает Category/PurchasePrice у продуктов, у которых они ещё не заданы.</summary>
    private static async Task BackfillProductMetaAsync(ApplicationDbContext db)
    {
        var products = await db.Products.ToListAsync();
        var changed = false;
        foreach (var p in products)
        {
            if (!ProductMeta.TryGetValue(p.Name, out var meta)) continue;
            if (p.Category == "Прочее" || string.IsNullOrEmpty(p.Category))
            { p.Category = meta.Category; changed = true; }
            if (p.PurchasePrice <= 0)
            { p.PurchasePrice = meta.Price; changed = true; }
        }
        if (changed) await db.SaveChangesAsync();
    }

    /// <summary>
    /// Полный сброс демо: удаляет все данные склада и организации,
    /// затем заново вызывает SeedAsync. Используется кнопкой «Сбросить демо».
    /// </summary>
    public static async Task ResetAndReseedAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        if (db.Database.IsNpgsql())
            await db.Database.EnsureCreatedAsync();
        else
            await db.Database.MigrateAsync();

        // Порядок удаления — от дочерних к родительским (FK = Restrict)
        db.Shifts.RemoveRange(db.Shifts);
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

    /// <summary>
    /// Демо-персонал и доступы: админ + пара сотрудников со сменами на сегодня.
    /// Идемпотентно — добивает только то, чего ещё нет, поэтому безопасно
    /// вызывать и на уже засиденной БД.
    /// </summary>
    private static async Task SeedStaffAsync(ApplicationDbContext db)
    {
        var org = await db.Organizations.FirstOrDefaultAsync();
        if (org is null) return; // склады ещё не созданы — сработает на следующем прогоне

        var restaurant = await db.Restaurants
            .Where(r => r.OrganizationId == org.Id)
            .FirstOrDefaultAsync();
        if (restaurant is null) return;
        var rid = restaurant.Id;

        async Task<AccessCode> EnsureCodeAsync(string code, AppRole role, string name)
        {
            var existing = await db.AccessCodes.FirstOrDefaultAsync(c => c.Code == code);
            if (existing is not null) return existing;
            var created = new AccessCode(code, role, org.Id) { EmployeeName = name, IsActive = true };
            db.AccessCodes.Add(created);
            await db.SaveChangesAsync();
            return created;
        }

        // Админ владеет разделом /Admin — код входит отдельно от менеджера
        await EnsureCodeAsync(AdminAccessCode, AppRole.Admin, "Демо-админ");

        // Линейный персонал для виджета «Смена» и расписания
        var rin = await EnsureCodeAsync("CHEF0001", AppRole.HeadChef, "Рин");
        var mika = await EnsureCodeAsync("COOK0001", AppRole.Staff, "Мика");

        // Смены на сегодня — чтобы виджет «Смена» ожил сразу после входа
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        async Task EnsureShiftAsync(AccessCode code, string position, TimeOnly start, TimeOnly end)
        {
            var exists = await db.Shifts.AnyAsync(s =>
                s.RestaurantId == rid && s.AccessCodeId == code.Id && s.Date == today);
            if (exists) return;
            db.Shifts.Add(new Shift
            {
                RestaurantId = rid,
                AccessCodeId = code.Id,
                Date = today,
                StartTime = start,
                EndTime = end,
                Position = position,
            });
        }

        await EnsureShiftAsync(rin, "шеф", new TimeOnly(9, 0), new TimeOnly(23, 0));
        await EnsureShiftAsync(mika, "повар", new TimeOnly(12, 0), new TimeOnly(20, 0));
        await db.SaveChangesAsync();
    }
}
