using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class OperationReceiptRepository(ApplicationDbContext context) : IOperationReceiptRepository
{
    public Task<OperationReceipt?> GetCompletedAsync(
        Guid shopId,
        FinancialOperationType operationType,
        string operationKey,
        CancellationToken cancellationToken = default) =>
        context.OperationReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.ShopId == shopId
                    && r.OperationType == operationType
                    && r.OperationKey == operationKey
                    && r.Status == OperationReceiptStatus.Completed,
                cancellationToken);
}
