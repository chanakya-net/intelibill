namespace Intelibill.Domain.Enums;

/// <summary>
/// How the money of a payment receipt moved, as the cashier recorded it. An external reference
/// (UPI or card) is recorded evidence only; it does not mean the shop verified gateway settlement.
/// </summary>
public enum ReceiptTenderMethod
{
    /// <summary>
    /// No tender was recorded. Every payment made before receipts existed is backfilled with this
    /// value; it is never guessed as cash.
    /// </summary>
    UnknownRecordedMethod = 1,
    Cash = 2,
    Upi = 3,
    Card = 4,
    BankTransfer = 5,
    Cheque = 6,
}
