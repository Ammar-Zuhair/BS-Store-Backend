using BSStore.Domain.Common;
using BSStore.Domain.Enums;

namespace BSStore.Domain.Entities;

public class Product : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public Guid StoreId { get; set; }
    public SourceType SourceType { get; set; }

    /// <summary>Amount the platform expects to pay the store. Never shown to customer.</summary>
    public decimal ExpectedPurchasePrice { get; set; }

    /// <summary>Amount the customer pays.</summary>
    public decimal SellingPrice { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;

    // Navigation
    public Category Category { get; set; } = null!;
    public Store Store { get; set; } = null!;
    public ICollection<ProductImage> Images { get; set; } = [];
    public Inventory? Inventory { get; set; }
    public ICollection<CartItem> CartItems { get; set; } = [];
}
