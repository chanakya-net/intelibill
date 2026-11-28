using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using static Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase.ResolveReconciliationCaseCommandHandler;

namespace Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase;

/// <summary>
/// Closes a case by linking its envelope to a sale: an existing sale the reviewer names, or the
/// sale a replay of the frozen envelope under its historical invoice lease finds or stages.
/// </summary>
public sealed class ReconciliationSaleLinker(
    IOfflineSubmissionRepository submissionRepository,
    ISaleRepository saleRepository,
    IInvoiceLeaseRepository invoiceLeaseRepository,
    IDiscountRuleRepository discountRuleRepository,
    IOfflineSalePoster salePoster,
    IOfflineExpiryAuthority expiryAuthority)
{
    internal async Task<InvoiceLease?> ResolveLeaseAsync(Guid shopId, OfflineSubmission? submission, CancellationToken ct)
    {
        if (submission is null) return null;
        if (submission.OriginalLeaseId is { } leaseId)
        {
            var byId = await invoiceLeaseRepository.GetByIdAsync(leaseId, ct);
            if (byId is not null && byId.ShopId == shopId) return byId;
        }
        return await invoiceLeaseRepository.FindReservedRangeAsync(shopId, submission.DeviceId, submission.InvoiceNumber, ct);
    }

    internal async Task<ErrorOr<Effect>> LinkExistingSaleAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        bool isHistoryCase,
        ResolutionRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var command = request.Command;
        if (command.LinkedOperationId is not { } saleId)
            return Errors.Reconciliation.LinkedOperationRequired;

        var sale = await saleRepository.GetByIdAsync(saleId, command.ShopId, cancellationToken);
        if (sale is null)
            return Errors.Reconciliation.SaleIdentityMismatch;

        var linkError = isHistoryCase
            ? await ReconciliationLinkRules.CheckHistorySaleAsync(issue, submission, sale, submissionRepository, cancellationToken)
            : await ReconciliationLinkRules.CheckSaleAsync(issue, submission, sale, submissionRepository, cancellationToken);
        if (linkError is { } refusedLink)
            return refusedLink;

        if (issue.IssueType == ReconciliationIssueType.ExpiryReview
            && (submission is null || !await expiryAuthority.VerifiesPostedSaleAsync(submission, sale, cancellationToken)))
        {
            return Errors.Reconciliation.ReplayRejected;
        }

        if (submission is not null && !ReconciliationLinkRules.IsIdempotencyConflict(issue) && submission.LinkSale(sale.Id).IsError)
            return Errors.Reconciliation.SaleIdentityMismatch;

        issue.LinkSale(sale.Id);
        return Close(issue, request, now, sale.Id, stockTransactionId: null);
    }

    /// <summary>
    /// Replays the frozen envelope. Staging a new sale goes through <see cref="IOfflineSalePoster.StageAsync"/>,
    /// which takes the posting-period, expiry and till locks after the caller's licence fence.
    /// </summary>
    internal async Task<ErrorOr<Effect>> RetryHistoricalLeaseUnderPostingLocksAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        InvoiceLease? resolvedLease,
        bool isHistoryCase,
        ShopLicenceLease? fencedLicence,
        ResolutionRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var command = request.Command;
        if (submission is null)
            return Errors.Reconciliation.EnvelopeMissing;
        if (submission.AttributableUserId is not { } issuer)
            return Errors.Reconciliation.ProvenanceUnverified;

        var lease = resolvedLease ?? await invoiceLeaseRepository.FindReservedRangeAsync(command.ShopId, submission.DeviceId, submission.InvoiceNumber, cancellationToken);
        if (lease is null)
            return Errors.Sale.InvoiceLeaseNotFound;

        var existing = await saleRepository.GetByClientSaleIdAsync(command.ShopId, submission.DeviceId, submission.ClientSaleId, cancellationToken);
        if (existing is not null)
        {
            if (isHistoryCase)
            {
                if (await ReconciliationLinkRules.CheckHistorySaleAsync(issue, submission, existing, submissionRepository, cancellationToken) is { } mismatch)
                    return mismatch;
            }
            else if (!string.Equals(existing.RequestHash, submission.PayloadHash, StringComparison.Ordinal))
            {
                return Errors.Sale.IdempotencyConflict;
            }

            if (issue.IssueType == ReconciliationIssueType.ExpiryReview && !await expiryAuthority.VerifiesPostedSaleAsync(submission, existing, cancellationToken))
                return Errors.Reconciliation.ReplayRejected;
            return LinkPostedSale(issue, submission, request, now, existing.Id);
        }

        if (await saleRepository.GetByInvoiceNumberAsync(command.ShopId, submission.InvoiceNumber, cancellationToken) is not null)
            return Errors.Sale.InvoiceNumberAlreadyUsed;

        // Every replay that creates a sale re-proves the stored command against the hash saved at first receipt.
        var payload = OfflineSubmissionPayload.TryReadValidated(command.ShopId, submission);
        if (payload is null)
            return Errors.Reconciliation.ReplayRejected;

        if ((isHistoryCase || request.Action == ReconciliationAction.ReconcileTillSale)
            && (payload.ClientSaleId.Trim() != submission.ClientSaleId
                || payload.InvoiceNumber.Trim() != submission.InvoiceNumber
                || OfflinePermitContractV1.Instant(payload.SoldAt) != OfflinePermitContractV1.Instant(submission.ClaimedSoldAt)))
        {
            return Errors.Reconciliation.ReplayRejected;
        }

        ReviewedTillContext? reviewedTill = null;
        if (request.Action == ReconciliationAction.ReconcileTillSale)
        {
            if (command.TillReconciliationEvidence is not { } evidence) return Errors.Till.SettlementEvidenceRequired;
            reviewedTill = new ReviewedTillContext(issue.Id, request.Reason, evidence);
        }

        var activeRules = await discountRuleRepository.GetActiveByShopAsync(command.ShopId, now, cancellationToken);
        var outcome = await salePoster.StageAsync(
            new OfflineSalePostingRequest(
                command.ShopId,
                submission.DeviceId,
                submission.ClientSaleId,
                payload,
                submission.PayloadHash,
                AttributedUserId: issuer,
                RecordedByUserId: command.ActorUserId,
                submission.Id,
                now,
                activeRules.ToDictionary(rule => rule.Id),
                lease,
                CreditReviewed: request.Action == ReconciliationAction.RetryHistoricalLease
                    && issue.IssueType != ReconciliationIssueType.TillReview
                    && !isHistoryCase,
                ReviewedTill: reviewedTill,
                FirstReceivedAt: submission.ReceivedAt,
                ReceivedPermitId: submission.LicencePermitId,
                ReceivedGrantId: submission.LicenceGrantId,
                IsExistingHistoryReview: isHistoryCase,
                ExistingLicence: fencedLicence),
            cancellationToken);

        return outcome switch
        {
            OfflineSalePostingOutcome.Staged staged => LinkPostedSale(issue, submission, request, now, staged.Sale.Id),
            OfflineSalePostingOutcome.Rejected rejected => new List<Error> { Errors.Reconciliation.ReplayRejected, rejected.Error },
            OfflineSalePostingOutcome.Held held => new List<Error> { Errors.Reconciliation.ReplayRejected, held.Error },
            _ => Errors.Reconciliation.ReplayRejected,
        };
    }

    private static ErrorOr<Effect> LinkPostedSale(
        ReconciliationIssue issue,
        OfflineSubmission submission,
        ResolutionRequest request,
        DateTimeOffset now,
        Guid saleId)
    {
        if (submission.LinkSale(saleId).IsError)
            return Errors.Reconciliation.SaleIdentityMismatch;

        issue.LinkSale(saleId);
        return Close(issue, request, now, saleId, stockTransactionId: null);
    }
}
