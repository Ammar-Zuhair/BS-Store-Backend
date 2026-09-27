using BSStore.Domain.Enums;

namespace BSStore.Application.Orders.DTOs;

public record ValidateCheckoutRequest(
    List<CheckoutItemInput> Items,
    Guid? AddressId,
    PaymentMethod PaymentMethod
);

public record CheckoutItemInput(
    Guid ProductId,
    int Quantity
);

public record CheckoutValidationResult(
    bool IsValid,
    List<string> Errors,
    decimal SubTotal,
    decimal DeliveryFee,
    decimal Discount,
    decimal TotalAmount,
    bool CodAllowed,
    decimal CodLimit,
    double EstimatedDistanceKm = 0
);

public record PlaceOrderRequest(
    Guid? AddressId,
    NewAddressInput? NewAddress,
    PaymentMethod PaymentMethod,
    string? Notes,
    string? IdempotencyKey
);

public record NewAddressInput(
    AddressLabel Label,
    string City,
    string District,
    string Street,
    string? Building,
    string? Description,
    decimal Latitude,
    decimal Longitude
);

public record OrderItemDto(
    Guid Id,
    Guid? ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal TotalPrice,
    decimal? ActualPurchasePrice
);

public record SubOrderDto(
    Guid Id,
    Guid StoreId,
    string StoreName,
    OrderStatus Status,
    decimal SubTotal,
    List<OrderItemDto> Items
);

public record OrderDto(
    Guid Id,
    string OrderNumber,
    OrderStatus Status,
    PaymentMethod PaymentMethod,
    PaymentStatus PaymentStatus,
    decimal SubTotal,
    decimal DeliveryFee,
    decimal Discount,
    decimal TotalAmount,
    string? Notes,
    DateTime CreatedAt,
    string? DriverName,
    string? DriverPhone,
    string DeliveryAddress,
    List<SubOrderDto> SubOrders
);

public record CancelOrderRequest(
    string ReasonCode,
    string? Description
);
