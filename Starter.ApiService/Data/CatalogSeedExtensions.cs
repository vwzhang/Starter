using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Starter.ApiService.Data;

public static class CatalogSeedExtensions
{
    public static async Task SeedCatalogSampleDataAsync(
        this CatalogDbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Categories.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Catalog sample data already exists.");
            return;
        }

        var now = DateTimeOffset.UtcNow;

        CatalogCategory CreateCategory(
            string name,
            string description,
            int displayOrder,
            params CatalogProduct[] products)
        {
            var category = new CatalogCategory
            {
                Id = Guid.NewGuid(),
                Name = name,
                Description = description,
                DisplayOrder = displayOrder,
                CreatedAt = now,
                UpdatedAt = now,
            };

            foreach (var product in products)
            {
                product.CategoryId = category.Id;
                product.CreatedAt = now;
                product.UpdatedAt = now;
                category.Products.Add(product);
            }

            return category;
        }

        static CatalogProduct CreateProduct(
            string name,
            string sku,
            string description,
            decimal price,
            int stockQuantity,
            bool isActive = true)
        {
            return new CatalogProduct
            {
                Id = Guid.NewGuid(),
                Name = name,
                Sku = sku,
                Description = description,
                Price = price,
                StockQuantity = stockQuantity,
                IsActive = isActive,
            };
        }

        var categories = new[]
        {
            CreateCategory(
                "Hardware",
                "Physical equipment used by internal teams.",
                10,
                CreateProduct("Developer Laptop", "HW-LAPTOP-14", "Standard engineering laptop with docking support.", 1899m, 18),
                CreateProduct("Docking Station", "HW-DOCK-USBC", "USB-C dock for desk setups.", 249m, 42),
                CreateProduct("Conference Camera", "HW-CAM-ROOM", "Meeting room camera kit.", 499m, 7)),
            CreateCategory(
                "Software",
                "Licensed tools and packaged software.",
                20,
                CreateProduct("Admin Portal License", "SW-ADMIN-SEAT", "Annual internal admin portal seat.", 39m, 250),
                CreateProduct("Reporting Add-on", "SW-REPORTING", "Dashboard and scheduled report module.", 129m, 80)),
            CreateCategory(
                "Services",
                "One-time onboarding and implementation services.",
                30,
                CreateProduct("Onboarding Package", "SV-ONBOARD-STD", "Guided setup for a new department.", 950m, 12),
                CreateProduct("Data Migration Sprint", "SV-DATA-MIG", "Assisted import and validation package.", 2400m, 4)),
            CreateCategory(
                "Subscriptions",
                "Recurring support and operations plans.",
                40,
                CreateProduct("Support Subscription", "SUB-SUPPORT-PRO", "Priority support for business teams.", 299m, 120),
                CreateProduct("Audit Archive", "SUB-AUDIT-ARCHIVE", "Long-term audit log retention.", 89m, 0, false)),
        };

        dbContext.Categories.AddRange(categories);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {CategoryCount} catalog categories and {ProductCount} products.",
            categories.Length,
            categories.Sum(category => category.Products.Count));
    }
}
