using BSStore.Domain.Common;

namespace BSStore.Domain.Entities;

public class DriverSettlement : BaseEntity
{
    public Guid DriverId { get; set; }
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }

    // Navigation
    public Driver Driver { get; set; } = null!;
}
