using Microsoft.EntityFrameworkCore;
using SmartInventory.Models.Inventory;
using SmartInventory.Models.Structure;

namespace SmartInventory.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations { get; set; } = null!;
    public DbSet<Restaurant> Restaurants { get; set; } = null!;
    public DbSet<AccessCode> AccessCodes { get; set; } = null!;

    // Предметное ядро склада
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Delivery> Deliveries { get; set; } = null!;
    public DbSet<Batch> Batches { get; set; } = null!;
    public DbSet<Dish> Dishes { get; set; } = null!;
    public DbSet<RecipeItem> RecipeItems { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
    public DbSet<WriteOff> WriteOffs { get; set; } = null!;
    public DbSet<LeadRequest> LeadRequests { get; set; } = null!;
    public DbSet<AiInsight> AiInsights { get; set; } = null!;
    public DbSet<SupportTicket> SupportTickets { get; set; } = null!;

    // Расписание смен
    public DbSet<Shift> Shifts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AccessCode>()
            .Property(c => c.Role)
            .HasConversion<string>()
            .HasMaxLength(30);

        // 1. Настройка уникального индекса для кодов доступа на всю систему
        modelBuilder.Entity<AccessCode>()
            .HasIndex(c => c.Code)
            .IsUnique();

        modelBuilder.Entity<Organization>()
            .HasMany(o => o.AccessCodes)
            .WithOne(c => c.Organization)
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade); // Если удаляется организация, удаляются и ее коды

        modelBuilder.Entity<Organization>()
            .HasMany(o => o.Restaurants)
            .WithOne(b => b.Organization)
            .HasForeignKey(b => b.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Авторизация ищет по OwnerId/ManagerId — без индексов будет скан
        modelBuilder.Entity<Organization>().HasIndex(o => o.OwnerId);
        modelBuilder.Entity<Restaurant>().HasIndex(r => r.ManagerId);

        // === Складское ядро: ограничения точности и осмысленные индексы ===

        var decimalProps = new (Type Entity, string[] Props)[]
        {
            (typeof(Batch), [nameof(Batch.QtyInitial), nameof(Batch.QtyRemaining)]),
            (typeof(Dish), [nameof(Dish.Price)]),
            (typeof(Product), [nameof(Product.PurchasePrice)]),
            (typeof(RecipeItem), [nameof(RecipeItem.Quantity)]),
            (typeof(WriteOff), [nameof(WriteOff.Qty)]),
        };
        foreach (var (entity, props) in decimalProps)
        {
            foreach (var prop in props)
            {
                modelBuilder.Entity(entity).Property(prop).HasPrecision(18, 4);
            }
        }

        // Мультитенантность: почти все запросы фильтруются по ресторану
        modelBuilder.Entity<Product>().HasIndex(p => p.RestaurantId);
        modelBuilder.Entity<Dish>().HasIndex(d => d.RestaurantId);
        modelBuilder.Entity<Order>().HasIndex(o => o.RestaurantId);
        modelBuilder.Entity<WriteOff>().HasIndex(w => w.RestaurantId);
        modelBuilder.Entity<AiInsight>().HasIndex(a => new { a.RestaurantId, a.CreatedAt });

        // FEFO-списание: поиск живых партий продукта по сроку годности
        modelBuilder.Entity<Batch>().HasIndex(b => new { b.ProductId, b.ExpiresAt });
        modelBuilder.Entity<Batch>().HasIndex(b => b.DeliveryId);

        // Разворот блюда в рецепт и заказа в позиции
        modelBuilder.Entity<RecipeItem>().HasIndex(r => r.DishId);
        modelBuilder.Entity<RecipeItem>().HasIndex(r => r.ProductId);
        modelBuilder.Entity<OrderItem>().HasIndex(o => o.OrderId);

        // Расписание смен: недельная сетка и виджет — всегда по ресторану и дате
        modelBuilder.Entity<Shift>().HasIndex(s => new { s.RestaurantId, s.Date });
        modelBuilder.Entity<Shift>()
            .HasOne(s => s.AccessCode)
            .WithMany()
            .HasForeignKey(s => s.AccessCodeId)
            .OnDelete(DeleteBehavior.Cascade);

        // У Order/Batch/Product несколько дочерних таблиц — каскадные удаления
        // создают циклические пути, запрещённые в SQL Server. Все складские FK
        // работают в режиме Restrict: историю партий/заказов удалять нельзя.
        var inventoryFks = new[]
        {
            modelBuilder.Entity<Batch>().Metadata.GetForeignKeys(),
            modelBuilder.Entity<RecipeItem>().Metadata.GetForeignKeys(),
            modelBuilder.Entity<OrderItem>().Metadata.GetForeignKeys(),
            modelBuilder.Entity<WriteOff>().Metadata.GetForeignKeys(),
        }.SelectMany(fks => fks);
        foreach (var fk in inventoryFks)
        {
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}