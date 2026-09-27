namespace BSStore.Domain.Entities;

/// <summary>
/// Payment configuration. Singleton row (Id = 1).
/// </summary>
public class PaymentSettings
{
    public int Id { get; set; } = 1;

    /// <summary>Orders with total &lt;= this limit can use Cash on Delivery.</summary>
    public decimal CashOnDeliveryLimit { get; set; } = 5000;

    public string? TransferBankName { get; set; }
    public string? TransferAccountNumber { get; set; }
    public string? TransferAccountName { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
