namespace Intelibill.Domain.Enums;

public enum PurchaseReceiptValueStatus
{
    /// <summary>The received total is known; the receipt can be corrected.</summary>
    Resolved = 1,

    /// <summary>
    /// Historical batch whose original receipt total cannot be established (for example a batch
    /// created by the old destructive correction). Its amounts are unknown, never estimated, and a
    /// financial correction is refused.
    /// </summary>
    LegacyUnresolved = 2,
}
