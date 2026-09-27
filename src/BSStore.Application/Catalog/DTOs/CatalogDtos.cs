using BSStore.Domain.Enums;

namespace BSStore.Application.Catalog.DTOs;

public record CategoryDto(
    Guid Id,
    string Name,
    string IconName,
    int SortOrder,
    int ProductsCount,
    bool IsActive = true
);

public record StoreDto(
    Guid Id,
    string Name,
    string Phone,
    string Address,
    decimal Latitude,
    decimal Longitude,
    string? ImageUrl,
    bool IsActive,
    List<Guid>? CategoryIds = null
);

public record ProductDto(
    Guid Id,
    string Name,
    string? Description,
    Guid CategoryId,
    string CategoryName,
    Guid StoreId,
    string StoreName,
    SourceType SourceType,
    decimal SellingPrice,
    int? AvailableStock,
    bool IsActive,
    List<string> Images
);

public record CreateProductRequest(
    string Name,
    string? Description,
    Guid CategoryId,
    Guid StoreId,
    SourceType SourceType,
    decimal ExpectedPurchasePrice,
    decimal SellingPrice,
    int? InitialStock,
    List<string>? Images = null
);

public record UpdateProductRequest(
    string Name,
    string? Description,
    Guid CategoryId,
    Guid StoreId,
    SourceType SourceType,
    decimal ExpectedPurchasePrice,
    decimal SellingPrice,
    bool IsActive,
    List<string>? Images = null
);
