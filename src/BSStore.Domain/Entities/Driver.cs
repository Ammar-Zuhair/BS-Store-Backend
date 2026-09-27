using BSStore.Domain.Common;
using BSStore.Domain.Enums;

namespace BSStore.Domain.Entities;

public class Driver : BaseEntity
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DriverStatus Status { get; set; } = DriverStatus.Offline;

    // Navigation
    public User User { get; set; } = null!;
    public ICollection<Order> Orders { get; set; } = [];
    public ICollection<DriverTransaction> Transactions { get; set; } = [];
    public ICollection<DriverSettlement> Settlements { get; set; } = [];
}
