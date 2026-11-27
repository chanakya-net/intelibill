using ErrorOr;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

internal sealed class OfflineSaleVarianceAnalyzer(
    IReconciliationIssueRepository reconciliationIssueRepository,
    ILicenseHistoryReviewObservationRepository? observationRepository = null,
    IUnitOfWork? unitOfWork = null)
    : IOfflineSaleVarianceAnalyzer
{
    // A resend of a held sale that fails the same way reuses the open case filed for its envelope
    // instead of filing another.
    public async Task<Guid> AddReviewIssueAsync(
        Guid shopId,
        Guid? saleId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        Guid actorUserId,
        CancellationToken cancellationToken,
        string? invoiceNumber = null,
        Guid? offlineSubmissionId = null)
    {
        if (issueType == ReconciliationIssueType.LicenseHistoryReview && offlineSubmissionId is { } typedEnvelope)
        {
            var canonical = await reconciliationIssueRepository.FindCanonicalHistoryCaseAsync(
                shopId, typedEnvelope, includeResolvedTypedCase: true, cancellationToken);
            if (canonical is not null)
                return canonical.Id;
        }

        var openIssueId = await reconciliationIssueRepository.FindOpenIssueIdAsync(
            shopId,
            clientSaleId.Trim(),
            deviceId.Trim(),
            issueType,
            code.Trim(),
            message.Trim(),
            offlineSubmissionId,
            cancellationToken);
        if (openIssueId is { } existingId)
        {
            return existingId;
        }

        var issue = ReconciliationIssue.Create(
            shopId,
            saleId,
            clientSaleId,
            deviceId,
            issueType,
            code,
            message,
            actorUserId,
            invoiceNumber: invoiceNumber,
            offlineSubmissionId: offlineSubmissionId);
        await reconciliationIssueRepository.AddAsync(issue, cancellationToken);
        return issue.Id;
    }

    public async Task<ReconciliationIssue> OpenOrAttachLicenceHistoryAsync(
        Guid shopId,
        Guid? saleId,
        string clientSaleId,
        string deviceId,
        string invoiceNumber,
        Guid offlineSubmissionId,
        Guid createdBy,
        Error reason,
        OfflineLicenceObservationFact? observation,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var existing = await reconciliationIssueRepository.FindCanonicalHistoryCaseAsync(
                shopId, offlineSubmissionId, includeResolvedTypedCase: true, cancellationToken)
            ?? await reconciliationIssueRepository.FindUnresolvedPrePostingByEnvelopeAsync(shopId, offlineSubmissionId, cancellationToken);
        if (existing is not null)
        {
            if (existing.Status != ReconciliationCaseStatus.Resolved && observation is not null)
                await AttachLicenceObservationAsync(existing, offlineSubmissionId, observation, observedAt, advanceVersionOnNewObservation: true, cancellationToken);
            return existing;
        }

        var issue = ReconciliationIssue.Create(
            shopId,
            saleId,
            clientSaleId,
            deviceId,
            ReconciliationIssueType.LicenseHistoryReview,
            reason.Code,
            reason.Description,
            createdBy,
            invoiceNumber: invoiceNumber,
            offlineSubmissionId: offlineSubmissionId);
        await reconciliationIssueRepository.AddAsync(issue, cancellationToken);
        if (unitOfWork is not null)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        if (observation is not null)
            await AttachLicenceObservationAsync(issue, offlineSubmissionId, observation, observedAt, advanceVersionOnNewObservation: false, cancellationToken);

        return issue;
    }

    public async Task AttachLicenceObservationAsync(
        ReconciliationIssue existingCase,
        Guid offlineSubmissionId,
        OfflineLicenceObservationFact observation,
        DateTimeOffset observedAt,
        bool advanceVersionOnNewObservation,
        CancellationToken cancellationToken)
    {
        if (observationRepository is null)
            return;

        var entity = LicenseHistoryReviewObservation.Create(
            existingCase.ShopId,
            existingCase.Id,
            offlineSubmissionId,
            observedAt,
            observation.ReasonCode,
            observation.Subreason,
            observation.ComparedField,
            observation.WindowStart,
            observation.WindowEnd,
            observation.ObservedAccountRevision,
            observation.ObservedShopRevision,
            observation.SourceCompletionCutoff,
            observation.CompetingSubmissionId);

        var inserted = await observationRepository.RecordAsync(entity, cancellationToken);
        if (inserted && advanceVersionOnNewObservation)
            existingCase.Touch(observedAt);
    }

    public async Task PersistReviewIssuesAsync(
        Guid saleId,
        IEnumerable<ReconciliationIssue> issues,
        CancellationToken cancellationToken)
    {
        foreach (var issue in issues)
        {
            issue.LinkSale(saleId);
            await reconciliationIssueRepository.AddAsync(issue, cancellationToken);
        }
    }
}
