using BSStore.Domain.Common;

namespace BSStore.Domain.Entities;

public class PaymentReceipt : BaseEntity
{
    public Guid PaymentId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string MimeType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Payment Payment { get; set; } = null!;
}
