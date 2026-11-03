using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IReconciliationIssueRepository : IRepository<ReconciliationIssue>
{
    Task<IReadOnlyList<ReconciliationIssue>> GetUnresolvedByShopAsync(
        Guid shopId,
        CancellationToken cancellationToken = default);

    Task<Guid?> FindOpenIssueIdAsync(
        Guid shopId,
        string clientSaleId,
        string deviceId,
        ReconciliationIssueType issueType,
        string code,
        string message,
        CancellationToken cancellationToken = default);
}
