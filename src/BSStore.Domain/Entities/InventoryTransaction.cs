using BSStore.Domain.Common;
using BSStore.Domain.Enums;

namespace BSStore.Domain.Entities;

/// <summary>
/// Immutable ledger record for every inventory movement.
/// Never modify or delete these records.
/// </summary>
public class InventoryTransaction : BaseEntity
{
    public Guid ProductId { get; set; }
    public InventoryTransactionType Type { get; set; }

    /// <summary>Positive = adds to stock. Negative = removes from stock.</summary>
    public int Quantity { get; set; }

    public string? Reference { get; set; }
    public Guid? OrderId { get; set; }
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }

    // Navigation
    public Product Product { get; set; } = null!;
    public Order? Order { get; set; }
}
