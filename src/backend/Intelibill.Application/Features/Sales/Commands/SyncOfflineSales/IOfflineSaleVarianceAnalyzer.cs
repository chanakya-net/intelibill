using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public interface IOfflineSaleVarianceAnalyzer
{
    Task AddReviewIssueAsync(
        Guid shopId,
        Guid? saleId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        Guid actorUserId,
        CancellationToken cancellationToken);

    Task PersistReviewIssuesAsync(
        Guid saleId,
        IEnumerable<ReconciliationIssue> issues,
        CancellationToken cancellationToken);
}
