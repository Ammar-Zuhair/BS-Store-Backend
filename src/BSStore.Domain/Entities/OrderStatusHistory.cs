using BSStore.Domain.Common;
using BSStore.Domain.Enums;

namespace BSStore.Domain.Entities;

public class OrderStatusHistory : BaseEntity
{
    public Guid OrderId { get; set; }
    public OrderStatus OldStatus { get; set; }
    public OrderStatus NewStatus { get; set; }
    public string ActorType { get; set; } = string.Empty; // "Customer", "Driver", "Admin", "System"
    public Guid? ActorId { get; set; }
    public string? Reason { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
}
