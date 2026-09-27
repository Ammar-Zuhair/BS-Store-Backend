using BSStore.Domain.Common;
using BSStore.Domain.Enums;

namespace BSStore.Domain.Entities;

public class Payment : BaseEntity
{
    public Guid OrderId { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public Guid? VerifiedBy { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? RejectionReason { get; set; }

    // Navigation
    public Order Order { get; set; } = null!;
    public ICollection<PaymentReceipt> Receipts { get; set; } = [];
}
