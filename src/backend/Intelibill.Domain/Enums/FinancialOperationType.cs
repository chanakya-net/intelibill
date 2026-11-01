namespace Intelibill.Domain.Enums;

/// <summary>
/// A non-repeatable financial command that carries a durable operation key. Stored as its number;
/// never renumber a member, because committed receipts keep it for the retention period.
/// </summary>
public enum FinancialOperationType
{
    CustomerPayment = 1,
    SupplierPayment = 2,
}
