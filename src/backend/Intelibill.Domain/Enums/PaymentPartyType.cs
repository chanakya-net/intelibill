namespace Intelibill.Domain.Enums;

/// <summary>Who a payment receipt was exchanged with. Stored as text; never renumber.</summary>
public enum PaymentPartyType
{
    /// <summary>Money collected from a customer.</summary>
    Customer = 1,

    /// <summary>Money paid to a supplier.</summary>
    Supplier = 2,
}
