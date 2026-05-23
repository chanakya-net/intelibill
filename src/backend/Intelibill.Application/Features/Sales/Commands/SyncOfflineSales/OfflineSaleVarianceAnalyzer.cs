using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

internal sealed class OfflineSaleVarianceAnalyzer(IReconciliationIssueRepository reconciliationIssueRepository)
    : IOfflineSaleVarianceAnalyzer
{
    public async Task AddReviewIssueAsync(
        Guid shopId,
        Guid? saleId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var issue = ReconciliationIssue.Create(
            shopId,
            saleId,
            clientSaleId,
            deviceId,
            issueType,
            code,
            message,
            actorUserId);
        await reconciliationIssueRepository.AddAsync(issue, cancellationToken);
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
