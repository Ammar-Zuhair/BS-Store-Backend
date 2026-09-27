using BSStore.Domain.Common;

namespace BSStore.Domain.Entities;

/// <summary>
/// Summary of current available stock for a product.
/// The source of truth is InventoryTransactions.
/// This is a cached/computed view only.
/// </summary>
public class Inventory : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>Cached available quantity. Recalculated from InventoryTransactions.</summary>
    public int Quantity { get; set; } = 0;

    // Navigation
    public Product Product { get; set; } = null!;
    public ICollection<InventoryTransaction> Transactions { get; set; } = [];
}
