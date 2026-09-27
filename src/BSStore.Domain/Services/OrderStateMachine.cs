using BSStore.Domain.Enums;
using BSStore.Domain.Exceptions;

namespace BSStore.Domain.Services;

/// <summary>
/// Enforces valid order state transitions.
/// All status changes MUST go through this service to prevent invalid transitions.
/// </summary>
public static class OrderStateMachine
{
    private static readonly Dictionary<OrderStatus, IReadOnlySet<OrderStatus>> AllowedTransitions = new()
    {
        [OrderStatus.PendingPayment] = new HashSet<OrderStatus>
        {
            OrderStatus.PaymentPendingVerification,
            OrderStatus.Confirmed,
            OrderStatus.Cancelled,
            OrderStatus.PaymentFailed
        },
        [OrderStatus.PaymentPendingVerification] = new HashSet<OrderStatus>
        {
            OrderStatus.Confirmed,
            OrderStatus.PaymentFailed
        },
        [OrderStatus.Confirmed] = new HashSet<OrderStatus>
        {
            OrderStatus.SearchingDriver,
            OrderStatus.Cancelled
        },
        [OrderStatus.SearchingDriver] = new HashSet<OrderStatus>
        {
            OrderStatus.DriverAssigned,
            OrderStatus.Cancelled
        },
        [OrderStatus.DriverAssigned] = new HashSet<OrderStatus>
        {
            OrderStatus.DriverAccepted
        },
        [OrderStatus.DriverAccepted] = new HashSet<OrderStatus>
        {
            OrderStatus.Purchasing,
            OrderStatus.WarehousePickup,
            OrderStatus.OutForDelivery
        },
        [OrderStatus.Purchasing] = new HashSet<OrderStatus>
        {
            OrderStatus.OutForDelivery
        },
        [OrderStatus.WarehousePickup] = new HashSet<OrderStatus>
        {
            OrderStatus.OutForDelivery
        },
        [OrderStatus.OutForDelivery] = new HashSet<OrderStatus>
        {
            OrderStatus.Delivered,
            OrderStatus.Returned
        },
        // Terminal states — no further transitions
        [OrderStatus.Delivered] = new HashSet<OrderStatus>(),
        [OrderStatus.Cancelled] = new HashSet<OrderStatus>(),
        [OrderStatus.PaymentFailed] = new HashSet<OrderStatus>(),
        [OrderStatus.Returned] = new HashSet<OrderStatus>()
    };

    /// <summary>
    /// Validates that a transition from currentStatus to newStatus is permitted.
    /// Throws InvalidOrderTransitionException if not allowed.
    /// </summary>
    public static void ValidateTransition(OrderStatus currentStatus, OrderStatus newStatus)
    {
        if (!AllowedTransitions.TryGetValue(currentStatus, out var allowed))
            throw new InvalidOrderTransitionException(currentStatus.ToString(), newStatus.ToString());

        if (!allowed.Contains(newStatus))
            throw new InvalidOrderTransitionException(currentStatus.ToString(), newStatus.ToString());
    }

    public static bool CanTransition(OrderStatus from, OrderStatus to)
    {
        return AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    public static bool IsTerminal(OrderStatus status) =>
        status is OrderStatus.Delivered
            or OrderStatus.Cancelled
            or OrderStatus.PaymentFailed
            or OrderStatus.Returned;
}
