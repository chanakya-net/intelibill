namespace Intelibill.Domain.Enums;

public enum CustomerLedgerEntryType
{
    SaleDue = 1,
    PaymentReceived = 2,
    ReturnCredit = 3,
    ReturnCreditReversal = 4,

    /// <summary>Reverses a recorded payment: the customer owes the reversed amount again.</summary>
    PaymentReversal = 5,
    SaleCancellationCredit = 6,
}

public static class CustomerLedgerEntryTypeExtensions
{
    public static decimal ToBalanceDelta(this CustomerLedgerEntryType entryType, decimal amount)
    {
        return entryType switch
        {
            CustomerLedgerEntryType.SaleDue => amount,
            CustomerLedgerEntryType.ReturnCreditReversal => amount,
            CustomerLedgerEntryType.PaymentReversal => amount,
            CustomerLedgerEntryType.PaymentReceived => -amount,
            CustomerLedgerEntryType.ReturnCredit => -amount,
            CustomerLedgerEntryType.SaleCancellationCredit => -amount,
            _ => throw new ArgumentOutOfRangeException(nameof(entryType), entryType, null)
        };
    }
}
