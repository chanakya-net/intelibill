using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.OfflineReconciliation;

/// <summary>
/// Builds the case DTOs the console reads: names every person once, renders the frozen invoice
/// from the envelope (with its lease window) and attaches the caller's available actions, the
/// evidence they may link and the audit trail.
/// </summary>
public sealed class ReconciliationCaseMapper(
    IReconciliationCaseEventRepository caseEventRepository,
    IUserRepository userRepository,
    IInvoiceLeaseRepository invoiceLeaseRepository,
    IInventoryAdjustmentRepository adjustmentRepository,
    ISaleRepository saleRepository,
    IOfflineSubmissionRepository submissionRepository,
    ITillRepository tillRepository,
    IOfflineExpiryAuthority expiryAuthority,
    TimeProvider timeProvider,
    OfflineTillReconciliationEligibility tillEligibility,
    LicenseHistoryReviewAssessment? historyAssessment = null)
{
    public async Task<IReadOnlyList<ReconciliationCaseSummaryDto>> ToSummariesAsync(
        IReadOnlyList<ReconciliationIssue> issues,
        CancellationToken cancellationToken)
    {
        if (issues.Count == 0)
            return [];

        var names = await userRepository.GetDisplayNamesAsync(
            issues[0].ShopId,
            issues.Select(i => i.CreatedBy).ToHashSet(),
            cancellationToken);

        return issues
            .Select(issue => new ReconciliationCaseSummaryDto(
                issue.Id,
                issue.Version,
                issue.InvoiceNumber,
                issue.ClientSaleId,
                issue.DeviceId,
                issue.IssueType.ToString(),
                issue.Status.ToString(),
                [issue.Code],
                issue.Message,
                issue.SaleId,
                issue.Resolution?.ToString(),
                issue.CreatedAt,
                Person(issue.CreatedBy, names),
                issue.ResolvedAt))
            .ToList();
    }

    public async Task<ErrorOr<ReconciliationCaseDto>> ToDetailOrErrorAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        ShopRole role,
        Guid actorUserId,
        CancellationToken cancellationToken,
        ShopLicenceLease? existingLicence = null)
    {
        var (dto, denial) = await BuildDetailAsync(issue, submission, role, actorUserId, existingLicence, cancellationToken);
        if (denial is { } err)
            return err;
        return dto;
    }

    /// <param name="issue">The case, as read (or as just saved).</param>
    /// <param name="submission">The case's envelope, when it has one.</param>
    /// <param name="role">The caller's live role, for the available actions.</param>
    /// <param name="actorUserId">The caller.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<ReconciliationCaseDto> ToDetailAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        ShopRole role,
        Guid actorUserId,
        CancellationToken cancellationToken,
        ShopLicenceLease? existingLicence = null) =>
        (await BuildDetailAsync(issue, submission, role, actorUserId, existingLicence, cancellationToken)).Dto;

    private async Task<(ReconciliationCaseDto Dto, Error? Denial)> BuildDetailAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        ShopRole role,
        Guid actorUserId,
        ShopLicenceLease? existingLicence,
        CancellationToken cancellationToken)
    {
        var events = await caseEventRepository.GetByCaseAsync(issue.ShopId, issue.Id, cancellationToken);

        var people = CollectPeople(issue, events, submission);

        var names = await userRepository.GetDisplayNamesAsync(issue.ShopId, people, cancellationToken);
        var lease = await LoadOriginalLeaseAsync(issue, submission, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var expiry = await VerifyExpiryReviewAsync(issue, submission, now, cancellationToken);

        LicenseHistoryCaseEvaluation? historyEval = historyAssessment is null
            ? null
            : await historyAssessment.EvaluateAsync(issue, submission, lease, now, existingLicence, targetLinkedSaleId: null, cancellationToken);

        var actions = AvailableActions(issue, submission, role, actorUserId, expiry, historyEval);
        var candidates = await LinkCandidatesAsync(issue, submission, actions, historyEval, cancellationToken);

        var late = submission?.SaleId is { } posted
            ? await tillRepository.FindLatePostingAsync(issue.ShopId, submission.Id, posted, cancellationToken)
            : null;

        var dto = ToCaseDto(issue, submission, lease, names, events, actions, candidates, late, historyEval);
        return (dto, historyEval?.Denial);
    }

    private static HashSet<Guid> CollectPeople(
        ReconciliationIssue issue,
        IReadOnlyList<ReconciliationCaseEvent> events,
        OfflineSubmission? submission)
    {
        var people = new HashSet<Guid> { issue.CreatedBy };
        if (issue.ResolvedBy is { } resolvedBy)
            people.Add(resolvedBy);
        foreach (var caseEvent in events)
            people.Add(caseEvent.ActorUserId);
        if (submission is not null)
        {
            people.Add(submission.SubmittedByUserId);
            if (submission.AssertedIssuerUserId is { } asserted)
                people.Add(asserted);
            if (submission.OriginalIssuerUserId is { } original)
                people.Add(original);
        }

        return people;
    }

    private async Task<InvoiceLease?> LoadOriginalLeaseAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        CancellationToken cancellationToken)
    {
        var lease = submission?.OriginalLeaseId is { } leaseId
            ? await invoiceLeaseRepository.GetByIdAsync(leaseId, cancellationToken)
            : null;
        if (lease is not null && lease.ShopId != issue.ShopId)
            lease = null;

        return lease;
    }

    private async Task<ExpiryReviewState> VerifyExpiryReviewAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var tillEligible = false;
        var expiryVerified = false;
        var expiryLinkVerified = false;
        if (issue.IssueType == ReconciliationIssueType.ExpiryReview && submission is not null)
        {
            var payload = OfflineSubmissionPayload.TryReadValidated(issue.ShopId, submission);
            expiryVerified = payload is not null && payload.Items.Where(x => !x.IsServiceLine).All(x => x.ExpiryEvidence is not null) &&
                !(await expiryAuthority.ValidateFrozenAsync(issue.ShopId, payload, now, cancellationToken)).IsError;
            tillEligible = expiryVerified && submission.AttributableUserId is { } issuer &&
                await tillEligibility.CanReconcileAsync(issue.ShopId, payload!, issuer, now, cancellationToken);
            var expiryPosted = await saleRepository.GetByClientSaleIdAsync(
                issue.ShopId,
                submission.DeviceId,
                submission.ClientSaleId,
                cancellationToken);
            expiryLinkVerified = expiryPosted is not null
                && await expiryAuthority.VerifiesPostedSaleAsync(submission, expiryPosted, cancellationToken);
        }

        return new ExpiryReviewState(tillEligible, expiryVerified, expiryLinkVerified);
    }

    private static IReadOnlyList<ReconciliationAction> AvailableActions(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        ShopRole role,
        Guid actorUserId,
        ExpiryReviewState expiry,
        LicenseHistoryCaseEvaluation? historyEval) =>
        historyEval?.Denial is not null
            ? []
            : ReconciliationActionPolicy.AvailableActions(
                issue,
                submission,
                role,
                actorUserId,
                expiry.ExpiryVerified,
                expiry.ExpiryLinkVerified,
                expiry.TillEligible,
                historyEval?.HasSavedObservations ?? false,
                historyEval?.RetryEligible ?? false,
                historyEval?.LinkEligible ?? false);

    private static ReconciliationCaseDto ToCaseDto(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        InvoiceLease? lease,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyList<ReconciliationCaseEvent> events,
        IReadOnlyList<ReconciliationAction> actions,
        IReadOnlyList<ReconciliationLinkCandidateDto> candidates,
        TillLatePosting? late,
        LicenseHistoryCaseEvaluation? historyEval) =>
        new(
            issue.Id,
            issue.Version,
            issue.InvoiceNumber,
            issue.ClientSaleId,
            issue.DeviceId,
            issue.IssueType.ToString(),
            issue.Status.ToString(),
            [issue.Code],
            issue.Message,
            issue.ItemId,
            issue.InventoryBatchId,
            issue.PrintedQuantity,
            issue.AvailableQuantity,
            issue.ConsumedQuantity,
            issue.UnresolvedQuantity,
            issue.SaleId,
            issue.Resolution?.ToString(),
            issue.ResolutionReason,
            issue.LinkedOperationId,
            issue.ResolutionStockTransactionId,
            issue.CreatedAt,
            Person(issue.CreatedBy, names),
            issue.ResolvedAt,
            issue.ResolvedBy is { } resolver ? Person(resolver, names) : null,
            submission is null ? null : ToSubmission(submission, lease, names),
            actions.Select(action => action.ToString()).ToList(),
            events
                .Select(e => new ReconciliationCaseEventDto(
                    e.Action.ToString(),
                    e.FromStatus.ToString(),
                    e.ToStatus.ToString(),
                    Person(e.ActorUserId, names),
                    e.Reason,
                    e.LinkedOperationId,
                    e.OccurredAt))
                .ToList(),
            candidates,
            late is null
                ? null
                : new(
                    late.Id,
                    late.OfflineSubmissionId,
                    late.SaleId,
                    late.CaseId,
                    late.SessionId,
                    late.CloseId,
                    late.SourceBusinessDate,
                    late.OccurredAt,
                    late.RecordedAt,
                    late.CashierId,
                    late.TenderLines,
                    late.EvidenceStatus,
                    late.ManagerReason,
                    late.IncludedInCount,
                    late.EvidenceReason),
            historyEval?.Dto);

    private readonly record struct ExpiryReviewState(bool TillEligible, bool ExpiryVerified, bool ExpiryLinkVerified);

    /// <summary>
    /// What the caller's link actions may name, filtered by the action's own checks. Detail only: one
    /// or two indexed reads per case, never per row of a list.
    /// </summary>
    private async Task<IReadOnlyList<ReconciliationLinkCandidateDto>> LinkCandidatesAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        IReadOnlyList<ReconciliationAction> actions,
        LicenseHistoryCaseEvaluation? historyEval,
        CancellationToken cancellationToken)
    {
        var candidates = new List<ReconciliationLinkCandidateDto>();

        if (actions.Contains(ReconciliationAction.LinkStockCorrection)
            && issue.ItemId is { } itemId
            && issue.InventoryBatchId is { } batchId
            && issue.UnresolvedQuantity is { } unresolved)
        {
            var adjustments = await adjustmentRepository.GetUnlinkedIncreasesAsync(issue.ShopId, itemId, batchId, cancellationToken);
            candidates.AddRange(adjustments
                .Where(adjustment => ReconciliationLinkRules.CoversShortage(adjustment, issue.ShopId, itemId, batchId, unresolved))
                .Select(adjustment => new ReconciliationLinkCandidateDto(
                    adjustment.Id,
                    ReconciliationLinkCandidateDto.AdjustmentKind,
                    adjustment.AdjustmentNumber,
                    adjustment.Quantity,
                    adjustment.PerformedAt)));
        }

        if (actions.Contains(ReconciliationAction.LinkExistingSale))
        {
            if (historyEval is { IsHistoryCase: true })
            {
                foreach (var sale in historyEval.ExactLinkSales)
                {
                    candidates.Add(new ReconciliationLinkCandidateDto(
                        sale.Id,
                        ReconciliationLinkCandidateDto.SaleKind,
                        sale.InvoiceNumber,
                        null,
                        sale.SoldAt));
                }

                return candidates;
            }

            var deviceId = submission?.DeviceId ?? issue.DeviceId;
            var clientSaleId = submission?.ClientSaleId ?? issue.ClientSaleId;
            var sales = new List<Sale>(2);
            if (await saleRepository.GetByClientSaleIdAsync(issue.ShopId, deviceId, clientSaleId, cancellationToken) is { } byIdentity)
                sales.Add(byIdentity);

            // The sale holding a contested number is listed too, but only when the link would accept it.
            if (string.Equals(issue.Code, Errors.Sale.InvoiceNumberAlreadyUsed.Code, StringComparison.Ordinal)
                && issue.InvoiceNumber is { } invoiceNumber
                && await saleRepository.GetByInvoiceNumberAsync(issue.ShopId, invoiceNumber, cancellationToken) is { } holder
                && sales.TrueForAll(sale => sale.Id != holder.Id))
            {
                sales.Add(holder);
            }

            foreach (var sale in sales)
            {
                if ((issue.IssueType != ReconciliationIssueType.ExpiryReview || submission is not null && await expiryAuthority.VerifiesPostedSaleAsync(submission, sale, cancellationToken)) &&
                    await ReconciliationLinkRules.CheckSaleAsync(issue, submission, sale, submissionRepository, cancellationToken) is null)
                {
                    candidates.Add(new ReconciliationLinkCandidateDto(
                        sale.Id,
                        ReconciliationLinkCandidateDto.SaleKind,
                        sale.InvoiceNumber,
                        null,
                        sale.SoldAt));
                }
            }
        }

        return candidates;
    }

    private static OfflineSubmissionDto? ToSubmission(
        OfflineSubmission submission,
        InvoiceLease? lease,
        IReadOnlyDictionary<Guid, string> names)
    {
        var invoice = ReadInvoice(submission.PayloadJson);
        if (invoice is null)
            return null;

        return new OfflineSubmissionDto(
            submission.Id,
            submission.Source.ToString(),
            submission.ClaimedSoldAt,
            submission.ReceivedAt,
            submission.PayloadHash,
            submission.OriginalLeaseId,
            lease?.ReservedAt,
            lease?.ExpiresAt,
            Person(submission.SubmittedByUserId, names),
            submission.AssertedIssuerUserId is { } asserted ? Person(asserted, names) : null,
            submission.OriginalIssuerUserId is { } original ? Person(original, names) : null,
            submission.RecoveryOperationId,
            invoice);
    }

    private static OfflineInvoiceSnapshotDto? ReadInvoice(string payloadJson)
    {
        OfflineSaleSyncCommand? sale;
        try
        {
            sale = OfflineSubmissionPayload.Deserialize(payloadJson);
        }
        catch (JsonException)
        {
            // The envelope is still evidence (hash, people, times); only its rendering is unavailable.
            return null;
        }

        if (!OfflineSubmissionPayload.HasValidHasherInputs(sale))
            return null;

        return new OfflineInvoiceSnapshotDto(
            sale!.InvoiceNumber,
            sale.SoldAt,
            sale.CustomerName,
            sale.CustomerPhone,
            sale.PaymentMethod.ToString(),
            sale.SubtotalBeforeDiscount,
            sale.TotalDiscountAmount,
            sale.TotalTaxAmount,
            sale.TotalAmount,
            sale.PaidAmount,
            sale.DueAmount,
            sale.Items
                .Select(line => new OfflineInvoiceSnapshotLineDto(
                    line.ItemName,
                    line.Barcode,
                    line.IsServiceLine ? null : line.BatchNumber,
                    line.Quantity,
                    line.SalesPrice,
                    line.TaxRatePercent,
                    line.TaxAmount,
                    line.TotalAmount, line.ExpiryEvidence))
                .ToList(), sale.Till, sale.TenderLines, sale.TillBusinessDate, sale.TillCashierId);
    }

    private static ReconciliationPersonDto Person(Guid userId, IReadOnlyDictionary<Guid, string> names) =>
        new(userId, names.TryGetValue(userId, out var name) ? name : ReconciliationPersonDto.UnknownDisplayName);
}
