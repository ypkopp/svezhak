using System.ComponentModel.DataAnnotations;
using SmartInventory.Models.Structure;

namespace SmartInventory.Models.Inventory;

public class Product
{
    public int Id { get; init; }
    public int RestaurantId { get; init; }

    [MaxLength(100)]
    public string Name { get; set; } = null!;

    // "кг", "л", "шт"
    [MaxLength(10)]
    public string Unit { get; set; } = null!;
}

public class Delivery
{
    public int Id { get; init; }
    public int RestaurantId { get; init; }

    [MaxLength(50)]
    public string Number { get; set; } = null!;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public List<Batch> Batches { get; init; } = [];
}

public class Batch
{
    public int Id { get; init; }
    public int RestaurantId { get; init; }

    public int DeliveryId { get; init; }
    public Delivery Delivery { get; init; } = null!;

    public int ProductId { get; init; }
    public Product Product { get; init; } = null!;

    public decimal QtyInitial { get; set; }
    public decimal QtyRemaining { get; set; }

    public DateTime ReceivedAt { get; set; }
    // Срок годности живёт ТОЛЬКО здесь — на уровне партии, а не продукта
    public DateTime ExpiresAt { get; set; }
}

public class Dish
{
    public int Id { get; init; }
    public int RestaurantId { get; init; }

    [MaxLength(100)]
    public string Name { get; set; } = null!;

    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;

    public List<RecipeItem> RecipeItems { get; init; } = [];
}

public class RecipeItem
{
    public int Id { get; init; }

    public int DishId { get; init; }
    public Dish Dish { get; init; } = null!;

    public int ProductId { get; init; }
    public Product Product { get; init; } = null!;

    // Норма расхода продукта на 1 порцию блюда (в единицах продукта)
    public decimal Quantity { get; set; }
}

public class Order
{
    public int Id { get; init; }
    public int RestaurantId { get; init; }

    // "accepted" / "rejected"
    [MaxLength(20)]
    public string Status { get; set; } = null!;

    // Заполняется при Status == "rejected": перечень всех дефицитных позиций
    [MaxLength(2000)]
    public string? RejectReason { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public List<OrderItem> Items { get; init; } = [];
}

public class OrderItem
{
    public int Id { get; init; }

    public int OrderId { get; init; }
    public Order Order { get; init; } = null!;

    public int DishId { get; init; }
    public Dish Dish { get; init; } = null!;

    public int Qty { get; set; }
}

public class Shift
{
    public int Id { get; init; }
    public int RestaurantId { get; init; }

    // Сотрудник — FK на AccessCode
    public int AccessCodeId { get; set; }
    public AccessCode AccessCode { get; set; } = null!;

    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    // "шеф" / "повар" / "менеджер"
    [MaxLength(20)]
    public string Position { get; set; } = null!;
}

public class WriteOff
{
    public int Id { get; init; }
    public int RestaurantId { get; init; }

    public int BatchId { get; init; }
    public Batch Batch { get; init; } = null!;

    public int ProductId { get; init; }
    public Product Product { get; init; } = null!;

    public decimal Qty { get; set; }

    // "order" / "expired"
    [MaxLength(20)]
    public string Reason { get; set; } = null!;

    // Заполняется для списаний по заказу
    public int? OrderId { get; init; }
    public Order? Order { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
