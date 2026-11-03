using Intelibill.Domain.Entities;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IReconciliationCaseEventRepository : IRepository<ReconciliationCaseEvent>
{
    /// <summary>The case's audit trail, oldest first.</summary>
    Task<IReadOnlyList<ReconciliationCaseEvent>> GetByCaseAsync(
        Guid shopId,
        Guid caseId,
        CancellationToken cancellationToken);
}
