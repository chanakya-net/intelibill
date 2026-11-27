using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Features.FinancialOperations.Queries.GetFinancialOperation;

public sealed record GetFinancialOperationQuery(Guid UserId, Guid ShopId, string OperationType, string OperationKey, SessionClaims? Session = null)
{
    public override string ToString() => nameof(GetFinancialOperationQuery);
}
