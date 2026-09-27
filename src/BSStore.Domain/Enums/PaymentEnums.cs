namespace BSStore.Domain.Enums;

public enum PaymentMethod
{
    CashOnDelivery,
    ManualTransfer
}

public enum PaymentStatus
{
    Pending,
    PendingVerification,
    Verified,
    Rejected,
    Failed
}
