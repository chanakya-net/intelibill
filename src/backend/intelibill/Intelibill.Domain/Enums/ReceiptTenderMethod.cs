namespace Intelibill.Domain.Enums;

/// <summary>
/// How the money of a payment receipt moved, as the cashier recorded it. An external reference
/// (UPI or card) is recorded evidence only; it does not mean the shop verified gateway settlement.
/// </summary>
public enum ReceiptTenderMethod
{
    /// <summary>
    /// No tender was recorded (a payment posted outside a till without a method); it is never
    /// guessed as cash and is never a till tender.
    /// </summary>
    UnknownRecordedMethod = 1,
    Cash = 2,
    Upi = 3,
    Card = 4,
    BankTransfer = 5,
    Cheque = 6,
}
