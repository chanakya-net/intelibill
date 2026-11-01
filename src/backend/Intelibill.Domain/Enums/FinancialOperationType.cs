namespace Intelibill.Domain.Enums;

/// <summary>
/// A non-repeatable financial command that carries a durable operation key. Stored as its number;
/// never renumber a member, because committed receipts keep it for the retention period.
/// </summary>
public enum FinancialOperationType
{
    CustomerPayment = 1,
    SupplierPayment = 2,

    /// <summary>The ordered row ids and row hashes of one keyed bulk stock intake.</summary>
    BulkIntakeManifest = 3,

    /// <summary>One committed row of a keyed bulk stock intake.</summary>
    BulkIntakeRow = 4,
}
