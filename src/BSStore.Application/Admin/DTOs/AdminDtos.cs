using BSStore.Domain.Enums;

namespace BSStore.Application.Admin.DTOs;

public record DashboardSummaryDto(
    int TotalOrdersToday,
    decimal TotalSalesToday,
    int PendingVerificationPayments,
    int SearchingDriverOrders,
    int ActiveDriversCount,
    int LowStockProductsCount
);

public record AssignDriverRequest(
    Guid DriverId
);

public record RejectPaymentRequest(
    string Reason
);

public record AddInventoryTransactionRequest(
    Guid ProductId,
    InventoryTransactionType Type,
    int Quantity,
    string? Reference,
    string? Note
);

public record CreateSettlementRequest(
    decimal Amount,
    string? Note
);

public record UpdateDeliverySettingsDto(
    bool IsEnabled,
    decimal BaseFee,
    decimal PerKmFee,
    decimal BaseDistanceKm,
    decimal FreeDeliveryRadius
);

public record UpdatePaymentSettingsDto(
    decimal CashOnDeliveryLimit,
    string? TransferBankName,
    string? TransferAccountNumber,
    string? TransferAccountName
);
