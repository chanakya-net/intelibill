using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

internal sealed class OfflineSaleVarianceAnalyzer(IReconciliationIssueRepository reconciliationIssueRepository)
    : IOfflineSaleVarianceAnalyzer
{
    // A resend of a held sale that fails the same way reuses its open case instead of filing another.
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
