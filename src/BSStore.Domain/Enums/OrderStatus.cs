namespace BSStore.Domain.Enums;

public enum OrderStatus
{
    PendingPayment,
    PaymentPendingVerification,
    Confirmed,
    SearchingDriver,
    DriverAssigned,
    DriverAccepted,
    Purchasing,
    WarehousePickup,
    OutForDelivery,
    Delivered,
    Cancelled,
    PaymentFailed,
    Returned,
    PendingAdminApproval
}
