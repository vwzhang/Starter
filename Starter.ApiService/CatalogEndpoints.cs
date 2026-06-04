using Microsoft.EntityFrameworkCore;
using Starter.ApiService.Data;
using Starter.Shared;

namespace Starter.ApiService;

internal static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dev/catalog")
            .WithTags("Dev");

        group.MapGet("/categories", ListCategoriesAsync)
            .WithName("ListCatalogCategories");

        group.MapPost("/categories", CreateCategoryAsync)
            .WithName("CreateCatalogCategory");

        group.MapPut("/categories/{id:guid}", UpdateCategoryAsync)
            .WithName("UpdateCatalogCategory");

        group.MapDelete("/categories/{id:guid}", DeleteCategoryAsync)
            .WithName("DeleteCatalogCategory");

        group.MapGet("/products", ListProductsAsync)
            .WithName("ListCatalogProducts");

        group.MapPost("/products", CreateProductAsync)
            .WithName("CreateCatalogProduct");

        group.MapPut("/products/{id:guid}", UpdateProductAsync)
            .WithName("UpdateCatalogProduct");

        group.MapDelete("/products/{id:guid}", DeleteProductAsync)
            .WithName("DeleteCatalogProduct");

        return app;
    }

    private static async Task<IResult> ListCategoriesAsync(
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var categories = await dbContext.Categories
            .AsNoTracking()
            .OrderBy(category => category.DisplayOrder)
            .ThenBy(category => category.Name)
            .Select(category => new CatalogCategoryDto(
                category.Id,
                category.Name,
                category.Description,
                category.DisplayOrder,
                category.Products.Count,
                category.CreatedAt,
                category.UpdatedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(categories);
    }

    private static async Task<IResult> CreateCategoryAsync(
        CatalogCategorySaveRequest request,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validationMessage = ValidateCategory(request);

        if (validationMessage is not null)
        {
            return Results.BadRequest(new CatalogErrorDto(validationMessage));
        }

        var now = DateTimeOffset.UtcNow;
        var category = new CatalogCategory
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        ApplyCategoryRequest(category, request, now);

        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/dev/catalog/categories/{category.Id}", await ToCategoryDtoAsync(dbContext, category.Id, cancellationToken));
    }

    private static async Task<IResult> UpdateCategoryAsync(
        Guid id,
        CatalogCategorySaveRequest request,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validationMessage = ValidateCategory(request);

        if (validationMessage is not null)
        {
            return Results.BadRequest(new CatalogErrorDto(validationMessage));
        }

        var category = await dbContext.Categories.SingleOrDefaultAsync(category => category.Id == id, cancellationToken);

        if (category is null)
        {
            return Results.NotFound();
        }

        ApplyCategoryRequest(category, request, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(await ToCategoryDtoAsync(dbContext, category.Id, cancellationToken));
    }

    private static async Task<IResult> DeleteCategoryAsync(
        Guid id,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var hasProducts = await dbContext.Products.AnyAsync(product => product.CategoryId == id, cancellationToken);

        if (hasProducts)
        {
            return Results.BadRequest(new CatalogErrorDto("Move or delete products before deleting this category."));
        }

        var deleted = await dbContext.Categories
            .Where(category => category.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> ListProductsAsync(
        CatalogDbContext dbContext,
        Guid? categoryId = null,
        string? search = null,
        bool? active = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Products
            .AsNoTracking()
            .Include(product => product.Category)
            .AsQueryable();

        if (categoryId is not null)
        {
            query = query.Where(product => product.CategoryId == categoryId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(product =>
                EF.Functions.Like(product.Name, pattern)
                || EF.Functions.Like(product.Sku, pattern)
                || (product.Description != null && EF.Functions.Like(product.Description, pattern)));
        }

        if (active is not null)
        {
            query = query.Where(product => product.IsActive == active.Value);
        }

        var products = await query
            .OrderBy(product => product.Category.DisplayOrder)
            .ThenBy(product => product.Category.Name)
            .ThenBy(product => product.Name)
            .Select(product => ToProductDto(product))
            .ToListAsync(cancellationToken);

        return Results.Ok(products);
    }

    private static async Task<IResult> CreateProductAsync(
        CatalogProductSaveRequest request,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validationMessage = await ValidateProductAsync(request, dbContext, null, cancellationToken);

        if (validationMessage is not null)
        {
            return Results.BadRequest(new CatalogErrorDto(validationMessage));
        }

        var now = DateTimeOffset.UtcNow;
        var product = new CatalogProduct
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        ApplyProductRequest(product, request, now);

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/dev/catalog/products/{product.Id}", await ToProductDtoAsync(dbContext, product.Id, cancellationToken));
    }

    private static async Task<IResult> UpdateProductAsync(
        Guid id,
        CatalogProductSaveRequest request,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validationMessage = await ValidateProductAsync(request, dbContext, id, cancellationToken);

        if (validationMessage is not null)
        {
            return Results.BadRequest(new CatalogErrorDto(validationMessage));
        }

        var product = await dbContext.Products.SingleOrDefaultAsync(product => product.Id == id, cancellationToken);

        if (product is null)
        {
            return Results.NotFound();
        }

        ApplyProductRequest(product, request, DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(await ToProductDtoAsync(dbContext, product.Id, cancellationToken));
    }

    private static async Task<IResult> DeleteProductAsync(
        Guid id,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var deleted = await dbContext.Products
            .Where(product => product.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static void ApplyCategoryRequest(
        CatalogCategory category,
        CatalogCategorySaveRequest request,
        DateTimeOffset updatedAt)
    {
        category.Name = request.Name.Trim();
        category.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        category.DisplayOrder = Math.Max(0, request.DisplayOrder);
        category.UpdatedAt = updatedAt;
    }

    private static void ApplyProductRequest(
        CatalogProduct product,
        CatalogProductSaveRequest request,
        DateTimeOffset updatedAt)
    {
        product.CategoryId = request.CategoryId;
        product.Name = request.Name.Trim();
        product.Sku = request.Sku.Trim().ToUpperInvariant();
        product.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        product.Price = Math.Max(0, request.Price);
        product.StockQuantity = Math.Max(0, request.StockQuantity);
        product.IsActive = request.IsActive;
        product.UpdatedAt = updatedAt;
    }

    private static string? ValidateCategory(CatalogCategorySaveRequest request)
    {
        var name = request.Name.Trim();
        var description = request.Description?.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            return "Category name is required.";
        }

        if (name.Length > 80)
        {
            return "Category name must be 80 characters or fewer.";
        }

        return description?.Length > 500
            ? "Category description must be 500 characters or fewer."
            : null;
    }

    private static async Task<string?> ValidateProductAsync(
        CatalogProductSaveRequest request,
        CatalogDbContext dbContext,
        Guid? productId,
        CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var sku = request.Sku.Trim().ToUpperInvariant();
        var description = request.Description?.Trim();

        if (request.CategoryId == Guid.Empty)
        {
            return "Category is required.";
        }

        if (!await dbContext.Categories.AnyAsync(category => category.Id == request.CategoryId, cancellationToken))
        {
            return "Category does not exist.";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return "Product name is required.";
        }

        if (name.Length > 120)
        {
            return "Product name must be 120 characters or fewer.";
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            return "SKU is required.";
        }

        if (sku.Length > 40)
        {
            return "SKU must be 40 characters or fewer.";
        }

        if (description?.Length > 1000)
        {
            return "Product description must be 1000 characters or fewer.";
        }

        if (request.Price < 0)
        {
            return "Price cannot be negative.";
        }

        if (request.StockQuantity < 0)
        {
            return "Stock cannot be negative.";
        }

        var skuExists = await dbContext.Products.AnyAsync(
            product => product.Sku == sku && (productId == null || product.Id != productId.Value),
            cancellationToken);

        return skuExists ? "SKU must be unique." : null;
    }

    private static async Task<CatalogCategoryDto> ToCategoryDtoAsync(
        CatalogDbContext dbContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Categories
            .AsNoTracking()
            .Where(category => category.Id == id)
            .Select(category => new CatalogCategoryDto(
                category.Id,
                category.Name,
                category.Description,
                category.DisplayOrder,
                category.Products.Count,
                category.CreatedAt,
                category.UpdatedAt))
            .SingleAsync(cancellationToken);
    }

    private static async Task<CatalogProductDto> ToProductDtoAsync(
        CatalogDbContext dbContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Products
            .AsNoTracking()
            .Include(product => product.Category)
            .Where(product => product.Id == id)
            .Select(product => ToProductDto(product))
            .SingleAsync(cancellationToken);
    }

    private static CatalogProductDto ToProductDto(CatalogProduct product)
    {
        return new CatalogProductDto(
            product.Id,
            product.CategoryId,
            product.Category.Name,
            product.Name,
            product.Sku,
            product.Description,
            product.Price,
            product.StockQuantity,
            product.IsActive,
            product.CreatedAt,
            product.UpdatedAt);
    }
}
