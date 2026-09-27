using BSStore.Domain.Common;
using BSStore.Domain.Enums;

namespace BSStore.Domain.Entities;

public class Address : BaseEntity
{
    public Guid CustomerId { get; set; }
    public AddressLabel Label { get; set; } = AddressLabel.Home;
    public string City { get; set; } = string.Empty;
    public string District { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string? Building { get; set; }
    public string? Description { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public bool IsDefault { get; set; } = false;

    // Navigation
    public Customer Customer { get; set; } = null!;
    public ICollection<Order> Orders { get; set; } = [];
}
