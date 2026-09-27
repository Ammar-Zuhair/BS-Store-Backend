using BSStore.Domain.Common;

namespace BSStore.Domain.Entities;

public class Cart : BaseEntity
{
    public Guid CustomerId { get; set; }

    // Navigation
    public Customer Customer { get; set; } = null!;
    public ICollection<CartItem> Items { get; set; } = [];
}
