namespace BSStore.Domain.Common;

/// <summary>
/// Base class for all domain entities with a UUID primary key and audit timestamps.
/// Primary key defaults to Guid.Empty to allow EF Core ChangeTracker to accurately detect new entities.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
