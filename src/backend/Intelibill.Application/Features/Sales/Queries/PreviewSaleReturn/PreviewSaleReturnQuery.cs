using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Queries.PreviewSaleReturn;

public sealed record PreviewSaleReturnQuery(
    Guid ActorUserId,
    Guid ShopId,
    Guid SaleId,
    decimal? DueReductionOverrideAmount,
    string? DueOverrideReason,
    IReadOnlyList<PreviewSaleReturnItemQuery> Items);

public sealed record PreviewSaleReturnItemQuery(
    Guid SaleItemId,
    decimal Quantity,
    SaleLineType LineType,
    SaleReturnCondition? Condition,
    decimal? ApprovedRefundAmount,
    string? Notes);
