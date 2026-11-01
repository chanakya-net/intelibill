using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Interfaces.Repositories;

public interface IOperationReceiptRepository
{
    /// <summary>A committed receipt in this scope, or null. Read-only; never reserves.</summary>
    Task<OperationReceipt?> GetCompletedAsync(
        Guid shopId,
        FinancialOperationType operationType,
        string operationKey,
        CancellationToken cancellationToken = default);
}
