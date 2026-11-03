namespace Intelibill.Domain.Enums;

/// <summary>
/// The GST registration a shop has declared. Stored as its number. Never inferred from a GSTIN:
/// a shop that has not declared one cannot issue documents.
/// </summary>
public enum TaxRegime
{
    NotDeclared = 0,
    Regular = 1,
    Composition = 2,
    Unregistered = 3,
}
