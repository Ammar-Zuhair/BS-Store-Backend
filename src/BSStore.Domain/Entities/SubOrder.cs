using BSStore.Domain.Common;
using BSStore.Domain.Enums;

namespace BSStore.Domain.Entities;

/// <summary>
/// Represents one store's portion of a multi-store order.
/// </summary>
public class SubOrder : BaseEntity
{
    public Guid OrderId { get; set; }
    public Guid StoreId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Confirmed;
    public decimal ExpectedPurchaseCost { get; set; }
    public decimal? ActualPurchaseCost { get; set; }
    public decimal SubTotal { get; set; }
    public string? Notes { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public ICollection<OrderItem> Items { get; set; } = [];
}
