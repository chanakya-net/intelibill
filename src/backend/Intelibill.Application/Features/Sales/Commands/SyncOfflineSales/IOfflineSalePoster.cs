using ErrorOr;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

/// <summary>
/// Turns one frozen offline invoice into a sale with its stock movements, ledger entry and variance
/// cases. Shared by the sync endpoint and by reconciliation, which replays stored envelopes.
/// </summary>
public interface IOfflineSalePoster
{
    /// <summary>The payload-only checks (header amounts and line shape) that need no database.</summary>
    ErrorOr<Success> CheckStructure(OfflineSaleSyncCommand sale);

    /// <summary>
    /// Adds the sale and everything it posts to the unit of work without saving. Lease, timing,
    /// issuer and invoice-number checks are the caller's. A rejected or held outcome adds nothing.
    /// The caller must have a transaction open: the first statement is the posting period fence, whose
    /// shared lock holds until that transaction ends. A closed month is held as
    /// <see cref="ReconciliationIssueType.ClosedPeriodReview"/>; a busy one is rejected (retryable).
    /// </summary>
    Task<OfflineSalePostingOutcome> StageAsync(OfflineSalePostingRequest request, CancellationToken cancellationToken);
}

/// <param name="ShopId">The shop the sale posts to.</param>
/// <param name="DeviceId">The trimmed device id.</param>
/// <param name="ClientSaleId">The trimmed client sale id.</param>
/// <param name="Sale">The frozen invoice.</param>
/// <param name="PayloadHash">The idempotency hash stored on the sale.</param>
/// <param name="AttributedUserId">Who issued the invoice: the sale's actor and the movements' performer.</param>
/// <param name="RecordedByUserId">Who is posting it now: creator of movements, ledger entries and cases.</param>
/// <param name="OfflineSubmissionId">The envelope the invoice came from, attached to every case.</param>
/// <param name="Now">The posting time, recorded as the sale's sync time.</param>
/// <param name="ActiveDiscountRulesById">The shop's discount rules active now.</param>
/// <param name="Lease">The lease whose range holds the invoice number; its frozen seller profile issues the document.</param>
/// <param name="CreditReviewed">True when a reviewer posts the sale: the customer credit check files no new case.</param>
public sealed record OfflineSalePostingRequest(
    Guid ShopId,
    string DeviceId,
    string ClientSaleId,
    OfflineSaleSyncCommand Sale,
    string PayloadHash,
    Guid AttributedUserId,
    Guid RecordedByUserId,
    Guid? OfflineSubmissionId,
    DateTimeOffset Now,
    IReadOnlyDictionary<Guid, DiscountRule> ActiveDiscountRulesById,
    InvoiceLease? Lease = null,
    bool CreditReviewed = false);

public abstract record OfflineSalePostingOutcome
{
    private OfflineSalePostingOutcome()
    {
    }

    /// <summary>The sale and its postings are in the unit of work, unsaved.</summary>
    public sealed record Staged(Sale Sale, IReadOnlyList<string> Warnings) : OfflineSalePostingOutcome;

    /// <summary>The invoice cannot post as sent; nothing was staged.</summary>
    public sealed record Rejected(Error Error) : OfflineSalePostingOutcome;

    /// <summary>The invoice must be reviewed before it posts; nothing was staged and no case was filed.</summary>
    public sealed record Held(
        ReconciliationIssueType IssueType,
        Error Error,
        string Message,
        IReadOnlyList<OfflineArithmeticMismatch> Mismatches) : OfflineSalePostingOutcome;
}
