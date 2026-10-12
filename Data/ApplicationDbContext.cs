using Microsoft.EntityFrameworkCore;
using SmartInventory.Models.Structure;

namespace SmartInventory.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations { get; set; } = null!;
    public DbSet<Restaurant> Restaurants { get; set; } = null!;
    public DbSet<AccessCode> AccessCodes { get; set; } = null!;

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
    }
}