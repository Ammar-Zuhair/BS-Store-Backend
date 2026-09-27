namespace BSStore.Domain.Entities;

/// <summary>
/// Delivery fee configuration. Singleton row (Id = 1).
/// </summary>
public class DeliverySettings
{
    public int Id { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public decimal BaseFee { get; set; } = 400;
    public decimal PerKmFee { get; set; } = 50;
    public decimal BaseDistanceKm { get; set; } = 1.0m;
    public decimal FreeDeliveryRadius { get; set; } = 0;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedBy { get; set; }
}
