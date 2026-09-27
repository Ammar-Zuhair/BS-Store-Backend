using BSStore.Domain.Common;

namespace BSStore.Domain.Entities;

/// <summary>
/// Immutable price snapshot at time of order. Product prices can change but this never does.
/// </summary>
public class OrderItem : BaseEntity
{
    public Guid SubOrderId { get; set; }
    public Guid? ProductId { get; set; } // nullable for soft-deleted products

    // Price snapshots — frozen at order creation time
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public decimal SellingPriceSnapshot { get; set; }
    public decimal ExpectedPurchasePriceSnapshot { get; set; }
    public decimal ExpectedProfitSnapshot { get; set; }

    // Set by driver after actual purchase (EXTERNAL_STORE products only)
    public decimal? ActualPurchasePrice { get; set; }

    public int Quantity { get; set; }
    public decimal TotalSellingPrice { get; set; }

    // Navigation
    public SubOrder SubOrder { get; set; } = null!;
    public Product? Product { get; set; }
}
