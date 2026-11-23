using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.OfflineReconciliation;

public sealed record LicenseHistoryCaseEvaluation(
    bool IsHistoryCase,
    bool HasSavedObservations,
    bool RetryEligible,
    bool LinkEligible,
    IReadOnlyList<Sale> ExactLinkSales,
    LicenceHistoryReviewDto? Dto,
    Error? Denial = null);

/// <summary>
/// Shared historical licence evaluation and exact-sale matching used by both case detail (GET)
/// and resolution (POST /resolve), so the console and server always agree on evidence and actions.
/// </summary>
public sealed class LicenseHistoryReviewAssessment(
    ILicenseHistoryReviewObservationRepository observationRepository,
    IShopLicenceFence licenceFence,
    IOfflineLicenceHistoryAuthority licenceHistory,
    IOfflineLicenceEvidenceStore evidenceStore,
    ISaleRepository saleRepository,
    IOfflineSubmissionRepository submissionRepository,
    IOfflineExpiryAuthority expiryAuthority,
    IUnitOfWork unitOfWork)
{
    private const int DtoVersion = 1;
    private const string SupportedCode = "OfflineLicenceHistory.Supported";

    public async Task<LicenseHistoryCaseEvaluation> EvaluateAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        InvoiceLease? lease,
        DateTimeOffset now,
        ShopLicenceLease? existingLicence,
        Guid? targetLinkedSaleId,
        CancellationToken cancellationToken)
    {
        var observations = await observationRepository.GetByCaseAsync(issue.ShopId, issue.Id, cancellationToken);
        var hasObservations = observations.Count > 0;
        var isHistoryCase = ReconciliationActionPolicy.IsLicenceHistoryCase(issue, hasObservations);
        if (!isHistoryCase)
            return new(false, false, false, false, [], null);

        var matchedSales = await FindExactLinkSalesAsync(issue, submission, targetLinkedSaleId, cancellationToken);
        var linkEligible = issue.Status != ReconciliationCaseStatus.Resolved && matchedSales.Count > 0;

        var payload = DeserializeValidatedPayload(issue.ShopId, submission, out var payloadCorrupted);
        var (assess, denial) = await AssessAuthorityAsync(issue, submission, lease, payload, payloadCorrupted, now, existingLicence, cancellationToken);
        if (denial is not null)
        {
            var deniedDto = await BuildDtoAsync(issue, submission, payload, observations, assess, denial, false, false, cancellationToken);
            return new(true, hasObservations, false, false, [], deniedDto, denial);
        }

        var retryEligible = issue.Status != ReconciliationCaseStatus.Resolved
            && !linkEligible
            && submission is not null
            && submission.SaleId is null
            && submission.AttributableUserId is not null
            && !payloadCorrupted
            && assess?.Decision == OfflineLicenceHistoryDecision.Supported;

        var dto = await BuildDtoAsync(issue, submission, payload, observations, assess, null, retryEligible, linkEligible, cancellationToken);
        return new(true, hasObservations, retryEligible, linkEligible, matchedSales, dto);
    }

    private async Task<IReadOnlyList<Sale>> FindExactLinkSalesAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        Guid? targetLinkedSaleId,
        CancellationToken cancellationToken)
    {
        if (submission is null || submission.Id != issue.OfflineSubmissionId || submission.ShopId != issue.ShopId)
            return [];

        var candidates = new List<Sale>(3);
        if (targetLinkedSaleId is { } targetId
            && await saleRepository.GetByIdAsync(targetId, issue.ShopId, cancellationToken) is { } byId)
        {
            candidates.Add(byId);
        }

        if (await saleRepository.GetByClientSaleIdAsync(issue.ShopId, submission.DeviceId, submission.ClientSaleId, cancellationToken) is { } byIdentity
            && candidates.TrueForAll(s => s.Id != byIdentity.Id))
        {
            candidates.Add(byIdentity);
        }

        if (submission.SaleId is { } linkedId
            && candidates.TrueForAll(s => s.Id != linkedId)
            && await saleRepository.GetByIdAsync(linkedId, issue.ShopId, cancellationToken) is { } bySubmission)
        {
            candidates.Add(bySubmission);
        }

        var verified = new List<Sale>(candidates.Count);
        foreach (var sale in candidates)
        {
            if (await ReconciliationLinkRules.CheckHistorySaleAsync(issue, submission, sale, submissionRepository, cancellationToken) is not null)
                continue;
            if (issue.IssueType == ReconciliationIssueType.ExpiryReview
                && !await expiryAuthority.VerifiesPostedSaleAsync(submission, sale, cancellationToken))
                continue;
            verified.Add(sale);
        }

        return verified;
    }

    private async Task<(OfflineLicenceHistoryResult? Result, Error? Denial)> AssessAuthorityAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        InvoiceLease? lease,
        OfflineSaleSyncCommand? payload,
        bool payloadCorrupted,
        DateTimeOffset now,
        ShopLicenceLease? existingLicence,
        CancellationToken cancellationToken)
    {
        var openedTransaction = false;
        try
        {
            ShopLicenceLease licence;
            if (existingLicence is { } provided)
            {
                licence = provided;
            }
            else
            {
                if (!unitOfWork.HasActiveTransaction)
                {
                    await unitOfWork.BeginTransactionAsync(cancellationToken);
                    openedTransaction = true;
                }

                var entered = await licenceFence.EnterAsync(issue.ShopId, LicenceAction.Recovery, cancellationToken);
                if (entered.IsError)
                    return (null, entered.FirstError);
                licence = entered.Value;
            }

            var decision = licence.CurrentDecision();
            if (decision.Enforced && decision.Denial is { } liveDenial)
                return (null, liveDenial);

            if (submission is null || payload is null || payloadCorrupted)
            {
                var err = submission is null ? Errors.OfflineLicenceHistory.EvidenceMissing : Errors.OfflineLicenceHistory.EvidenceInvalid;
                var sub = submission is null ? "EnvelopeMissing" : "PayloadCorrupted";
                return (new(OfflineLicenceHistoryDecision.Hold, err,
                    new(err.Code, sub, "payloadJson", ObservedAccountRevision: licence.AccountRevision,
                        ObservedShopRevision: licence.ShopRevision, SourceCompletionCutoff: decision.SourceCompletionCutoff),
                    SourceCompletionCutoff: decision.SourceCompletionCutoff), null);
            }

            var request = new OfflineSalePostingRequest(
                issue.ShopId,
                submission.DeviceId,
                submission.ClientSaleId,
                payload,
                submission.PayloadHash,
                AttributedUserId: submission.AttributableUserId ?? Guid.Empty,
                RecordedByUserId: submission.SubmittedByUserId,
                submission.Id,
                now,
                new Dictionary<Guid, DiscountRule>(),
                lease,
                FirstReceivedAt: submission.ReceivedAt,
                ReceivedPermitId: submission.LicencePermitId,
                ReceivedGrantId: submission.LicenceGrantId,
                IsExistingHistoryReview: true,
                ExistingLicence: licence);

            var assessed = await licenceHistory.AssessAsync(licence, request, existingHistoryHold: true, cancellationToken);
            if (assessed is null)
            {
                return (new(OfflineLicenceHistoryDecision.Hold, Errors.OfflineLicenceHistory.EvidenceMissing,
                    new(Errors.OfflineLicenceHistory.EvidenceMissing.Code, "Unknown"),
                    SourceCompletionCutoff: decision.SourceCompletionCutoff), null);
            }

            if (assessed.Decision == OfflineLicenceHistoryDecision.Denied && assessed.Error is { } denied)
                return (assessed, denied);

            return (assessed, null);
        }
        finally
        {
            if (openedTransaction)
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }
    }

    private async Task<LicenceHistoryReviewDto> BuildDtoAsync(
        ReconciliationIssue issue,
        OfflineSubmission? submission,
        OfflineSaleSyncCommand? payload,
        IReadOnlyList<LicenseHistoryReviewObservation> observations,
        OfflineLicenceHistoryResult? assess,
        Error? denial,
        bool retryEligible,
        bool linkEligible,
        CancellationToken cancellationToken)
    {
        var retainedPermit = assess?.MatchedPermit;
        if (retainedPermit is null && submission?.LicencePermitId is { } permitId)
            retainedPermit = await evidenceStore.FindPermitByIdAsync(issue.ShopId, permitId, cancellationToken);

        var evidence = payload?.LicenceEvidence;
        var currentCode = denial?.Code
            ?? (assess?.Decision == OfflineLicenceHistoryDecision.Supported
                ? SupportedCode
                : assess?.Error?.Code ?? issue.Code);
        var currentSubreason = denial is { } deniedError
            ? (string.Equals(deniedError.Code, Errors.Licence.SecuritySuspended.Code, StringComparison.Ordinal) ? "SecuritySuspended" : "LicenceDenied")
            : (assess?.Decision == OfflineLicenceHistoryDecision.Supported
                ? "Supported"
                : assess?.Observation?.Subreason ?? "Unknown");

        return new LicenceHistoryReviewDto(
            DtoVersion,
            currentCode,
            currentSubreason,
            retryEligible,
            linkEligible,
            observations.Count > 0,
            submission?.ReceivedAt,
            evidence?.Id("permitId"),
            submission?.LicencePermitId,
            evidence?.Id("grantId"),
            submission?.LicenceGrantId,
            evidence?.Id("leaseId"),
            submission?.OriginalLeaseId,
            evidence?.Id("snapshotId"),
            evidence?.SafeKeyId(),
            retainedPermit?.KeyId,
            retainedPermit?.NotBefore,
            retainedPermit?.ValidUntil,
            assess?.SourceCompletionCutoff,
            observations.Select(o => new LicenceHistoryObservationDto(
                o.ReasonCode,
                o.Subreason,
                o.ObservedAt,
                o.ComparedField,
                o.WindowStart,
                o.WindowEnd,
                o.ObservedAccountRevision,
                o.ObservedShopRevision,
                o.SourceCompletionCutoff,
                o.CompetingSubmissionId)).ToList());
    }

    private static OfflineSaleSyncCommand? DeserializeValidatedPayload(
        Guid shopId,
        OfflineSubmission? submission,
        out bool corrupted)
    {
        corrupted = false;
        if (submission is null) return null;

        var payload = OfflineSubmissionPayload.TryReadValidated(shopId, submission);
        if (payload is null)
        {
            corrupted = true;
            return null;
        }

        return payload;
    }
}
