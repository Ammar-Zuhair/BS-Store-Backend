using BSStore.Domain.Common;

namespace BSStore.Domain.Entities;

public class ProductImage : BaseEntity
{
    public Guid ProductId { get; set; }
    public string ImageKey { get; set; } = string.Empty;
    public string? Url { get; set; }
    public int SortOrder { get; set; } = 0;

    // Navigation
    public Product Product { get; set; } = null!;
}
