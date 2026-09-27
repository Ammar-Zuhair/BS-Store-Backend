using BSStore.Domain.Common;
using BSStore.Domain.Enums;

namespace BSStore.Domain.Entities;

public class Order : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Guid? DriverId { get; set; }
    public Guid AddressId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public decimal SubTotal { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal Discount { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Notes { get; set; }

    /// <summary>Used to prevent duplicate order submissions from client retries.</summary>
    public string? IdempotencyKey { get; set; }

    // Navigation
    public Customer Customer { get; set; } = null!;
    public Driver? Driver { get; set; }
    public Address Address { get; set; } = null!;
    public ICollection<SubOrder> SubOrders { get; set; } = [];
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = [];
    public Payment? Payment { get; set; }
    public ICollection<InventoryTransaction> InventoryTransactions { get; set; } = [];
    public ICollection<DriverTransaction> DriverTransactions { get; set; } = [];
    public OrderCancellation? Cancellation { get; set; }
}
