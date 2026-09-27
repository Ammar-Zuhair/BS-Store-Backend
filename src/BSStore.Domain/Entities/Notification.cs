using BSStore.Domain.Common;

namespace BSStore.Domain.Entities;

public class Notification : BaseEntity
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public bool IsRead { get; set; } = false;

    // Navigation
    public User User { get; set; } = null!;
}
