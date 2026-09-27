using BSStore.Domain.Enums;

namespace BSStore.Application.Payments.DTOs;

public record TransferInfoDto(
    Guid OrderId,
    string OrderNumber,
    decimal Amount,
    string BankName,
    string AccountNumber,
    string AccountName,
    PaymentStatus Status,
    string? RejectionReason
);

public record UploadReceiptResponse(
    Guid ReceiptId,
    string ReceiptUrl,
    PaymentStatus NewStatus
);
