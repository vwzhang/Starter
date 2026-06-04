namespace Starter.Shared;

public sealed record CatalogCategoryDto(
    Guid Id,
    string Name,
    string? Description,
    int DisplayOrder,
    int ProductCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CatalogCategorySaveRequest(
    string Name,
    string? Description,
    int DisplayOrder);

public sealed record CatalogProductDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string Name,
    string Sku,
    string? Description,
    decimal Price,
    int StockQuantity,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CatalogProductSaveRequest(
    Guid CategoryId,
    string Name,
    string Sku,
    string? Description,
    decimal Price,
    int StockQuantity,
    bool IsActive);

public sealed record CatalogErrorDto(string Message);
