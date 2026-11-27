using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

namespace Intelibill.Application.Features.OfflineReconciliation.DTOs;

/// <summary>The outcome of one recovery submission: one result per bundle invoice, in bundle order.</summary>
public sealed record RecoverySubmissionResultDto(
    string OperationId,
    IReadOnlyList<RecoveryInvoiceResultDto> Results);

/// <param name="ClientSaleId">The invoice's client sale id, trimmed.</param>
/// <param name="InvoiceNumber">The invoice number, trimmed.</param>
/// <param name="Status">One of <see cref="RecoveryInvoiceStatus"/>.</param>
/// <param name="CaseId">The review case filed (or reused) for the invoice.</param>
/// <param name="SaleId">The sale that already carries this exact invoice (<c>AlreadyPosted</c> only).</param>
/// <param name="Errors">Why the invoice was rejected, held or escalated; empty when already posted.</param>
public sealed record RecoveryInvoiceResultDto(
    string ClientSaleId,
    string InvoiceNumber,
    string Status,
    Guid? CaseId,
    Guid? SaleId,
    IReadOnlyList<OfflineSaleSyncErrorDto> Errors);

public static class RecoveryInvoiceStatus
{
    /// <summary>Stored and filed as an open case for an owner or manager to approve, link or escalate.</summary>
    public const string ReviewOpened = "ReviewOpened";

    /// <summary>Stored, but nobody can be vouched for as its issuer: filed as an escalated case.</summary>
    public const string Escalated = "Escalated";

    /// <summary>A sale with the same device, client sale id and payload exists; the envelope was linked to it.</summary>
    public const string AlreadyPosted = "AlreadyPosted";

    /// <summary>Refused before anything was stored.</summary>
    public const string Rejected = "Rejected";
}
