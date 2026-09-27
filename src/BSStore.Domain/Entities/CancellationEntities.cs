using BSStore.Domain.Common;

namespace BSStore.Domain.Entities;

public class CancellationReason : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public ICollection<OrderCancellation> OrderCancellations { get; set; } = [];
}

public class OrderCancellation : BaseEntity
{
    public Guid OrderId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid CancelledBy { get; set; }

    public Order Order { get; set; } = null!;
    public CancellationReason Reason { get; set; } = null!;
}
