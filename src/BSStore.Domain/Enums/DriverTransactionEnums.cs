namespace BSStore.Domain.Enums;

public enum DriverTransactionType
{
    CustomerCollection,
    StorePurchase,
    CompanySettlement,
    DriverFunding,
    Refund,
    Adjustment,
    Other
}

public enum TransactionDirection
{
    Debit,
    Credit
}
