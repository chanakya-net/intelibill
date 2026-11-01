namespace Intelibill.Application.Features.FinancialOperations.Queries.GetFinancialOperation;

public sealed record GetFinancialOperationQuery(Guid UserId, Guid ShopId, string OperationType, string OperationKey);
