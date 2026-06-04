using Microsoft.EntityFrameworkCore;

namespace Starter.ApiService.Data;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<CatalogCategory> Categories => Set<CatalogCategory>();
    public DbSet<CatalogProduct> Products => Set<CatalogProduct>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<CatalogCategory>(entity =>
        {
            entity.ToTable("CatalogCategories");
            entity.HasIndex(category => category.Name).IsUnique();
            entity.HasIndex(category => category.DisplayOrder);
            entity.Property(category => category.Name).HasMaxLength(80);
            entity.Property(category => category.Description).HasMaxLength(500);
        });

        builder.Entity<CatalogProduct>(entity =>
        {
            entity.ToTable("CatalogProducts");
            entity.HasIndex(product => product.CategoryId);
            entity.HasIndex(product => product.IsActive);
            entity.HasIndex(product => product.Sku).IsUnique();
            entity.Property(product => product.Name).HasMaxLength(120);
            entity.Property(product => product.Sku).HasMaxLength(40);
            entity.Property(product => product.Description).HasMaxLength(1000);
            entity.Property(product => product.Price).HasPrecision(18, 2);

            entity.HasOne(product => product.Category)
                .WithMany(category => category.Products)
                .HasForeignKey(product => product.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
