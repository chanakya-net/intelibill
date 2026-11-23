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
    // A resend of a held sale that fails the same way reuses its open case instead of filing another:
    // first the case filed for this envelope; failing that, a legacy case for the same hold that was
    // filed before envelopes existed, which adopts the envelope as its evidence.
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
            var canonical = await reconciliationIssueRepository.FindCanonicalHistoryCaseAsync(shopId, typedEnvelope, cancellationToken);
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

        if (offlineSubmissionId is { } envelopeId && invoiceNumber is not null)
        {
            var legacy = await reconciliationIssueRepository.FindOpenLegacyIssueAsync(
                shopId,
                clientSaleId.Trim(),
                deviceId.Trim(),
                issueType,
                code.Trim(),
                message.Trim(),
                cancellationToken);
            if (legacy is not null)
            {
                legacy.AttachEvidence(invoiceNumber, envelopeId);
                return legacy.Id;
            }
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
        var existing = await reconciliationIssueRepository.FindCanonicalHistoryCaseAsync(shopId, offlineSubmissionId, cancellationToken)
            ?? await reconciliationIssueRepository.FindUnresolvedPrePostingByEnvelopeAsync(shopId, offlineSubmissionId, cancellationToken);
        if (existing is not null)
        {
            if (observation is not null)
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
