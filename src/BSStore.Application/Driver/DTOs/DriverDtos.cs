using BSStore.Domain.Enums;

namespace BSStore.Application.Driver.DTOs;

public record DriverStatusDto(
    Guid DriverId,
    string FullName,
    string Phone,
    DriverStatus Status,
    bool IsOnline,
    int CompletedTodayCount
);

public record UpdateDriverStatusRequest(
    DriverStatus Status
);

public record RecordPurchaseRequest(
    Guid SubOrderId,
    decimal ActualPurchasePrice,
    List<ItemPurchaseDetail>? ItemDetails
);

public record ItemPurchaseDetail(
    Guid OrderItemId,
    decimal ActualPrice
);

public record DriverFinancialSummaryDto(
    decimal TotalCustomerCollections,
    decimal TotalStorePurchases,
    decimal TotalSettlements,
    decimal TotalFundings,
    decimal NetOutstandingDebt
);

public record DriverTransactionDto(
    Guid Id,
    DriverTransactionType Type,
    decimal Amount,
    TransactionDirection Direction,
    string? Reference,
    string Description,
    DateTime CreatedAt
);
