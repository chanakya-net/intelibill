namespace Intelibill.Domain.Enums;

/// <summary>
/// The owner's attestation of whether e-invoicing (IRN) applies to the registration. Stored as its number.
/// </summary>
public enum EInvoiceStatus
{
    NotAttested = 0,

    /// <summary>Aggregate turnover has never exceeded the e-invoicing threshold in any year since 2017-18.</summary>
    BelowThreshold = 1,

    /// <summary>Turnover exceeds the threshold but the registration belongs to a notified exempt class.</summary>
    Exempt = 2,

    /// <summary>E-invoicing applies; documents cannot be issued until an IRP integration exists.</summary>
    Required = 3,
}
