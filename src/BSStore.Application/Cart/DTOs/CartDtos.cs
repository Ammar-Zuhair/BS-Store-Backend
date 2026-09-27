using BSStore.Domain.Enums;

namespace BSStore.Application.Cart.DTOs;

public record CartItemDto(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string? StoreName,
    decimal UnitPrice,
    int Quantity,
    decimal TotalPrice,
    string? Notes,
    string? ImageUrl,
    SourceType SourceType
);

public record CartDto(
    Guid Id,
    List<CartItemDto> Items,
    decimal SubTotal,
    int TotalItems
);

public record AddToCartRequest(
    Guid ProductId,
    int Quantity,
    string? Notes
);

public record UpdateCartItemRequest(
    int Quantity,
    string? Notes
);
