namespace Intelibill.Domain.Enums;

public enum CustomerLedgerEntryType
{
    SaleDue = 1,
    PaymentReceived = 2,
    ReturnCredit = 3,
    ReturnCreditReversal = 4
}

public static class CustomerLedgerEntryTypeExtensions
{
    public static decimal ToBalanceDelta(this CustomerLedgerEntryType entryType, decimal amount)
    {
        return entryType switch
        {
            CustomerLedgerEntryType.SaleDue => amount,
            CustomerLedgerEntryType.ReturnCreditReversal => amount,
            CustomerLedgerEntryType.PaymentReceived => -amount,
            CustomerLedgerEntryType.ReturnCredit => -amount,
            _ => throw new ArgumentOutOfRangeException(nameof(entryType), entryType, null)
        };
    }
}
