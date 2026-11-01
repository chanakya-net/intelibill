namespace Intelibill.Domain.Enums;

/// <summary>Where a receipt's authoritative value came from.</summary>
public enum PurchaseReceiptSource
{
    /// <summary>Single or bulk stock intake: the payable was posted from the entered total.</summary>
    DirectInbound = 1,

    /// <summary>A purchase order receipt line, which also keeps the received total.</summary>
    PurchaseOrder = 2,

    /// <summary>Reconstructed by migration from evidence that existed before receipt values.</summary>
    LegacyBackfill = 3,
}
