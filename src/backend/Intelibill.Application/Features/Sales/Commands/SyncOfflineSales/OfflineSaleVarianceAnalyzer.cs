using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

internal sealed class OfflineSaleVarianceAnalyzer(IReconciliationIssueRepository reconciliationIssueRepository)
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
