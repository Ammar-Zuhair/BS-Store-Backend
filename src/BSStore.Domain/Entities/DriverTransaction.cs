using BSStore.Domain.Common;
using BSStore.Domain.Enums;

namespace BSStore.Domain.Entities;

/// <summary>
/// Immutable financial ledger entry for a driver.
/// Never modify or delete. Use ADJUSTMENT to correct errors.
/// </summary>
public class DriverTransaction : BaseEntity
{
    public Guid DriverId { get; set; }
    public DriverTransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public TransactionDirection Direction { get; set; }
    public string? Reference { get; set; }
    public Guid? OrderId { get; set; }
    public string Description { get; set; } = string.Empty;
    public Guid CreatedBy { get; set; }

    // Navigation
    public Driver Driver { get; set; } = null!;
    public Order? Order { get; set; }
}
