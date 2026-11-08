namespace Intelibill.Domain.ValueObjects;

/// <summary>Signed settlement changes; none represents money in the drawer.</summary>
public sealed record TillNonCashSettlement(decimal StoreCreditIssued = 0, decimal StoreCreditRedeemed = 0, decimal ReceivableChange = 0);
