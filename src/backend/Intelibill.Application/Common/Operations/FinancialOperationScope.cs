using Intelibill.Domain.Enums;

namespace Intelibill.Application.Common.Operations;

/// <summary>The uniqueness scope of one financial operation: shop, type and key — never the actor.</summary>
public sealed record FinancialOperationScope(Guid ShopId, FinancialOperationType OperationType, string Key);
