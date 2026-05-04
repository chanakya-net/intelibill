using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;

public sealed record RecordSaleReturnCommand(
    Guid ActorUserId,
    Guid ShopId,
    Guid SaleId,
    PaymentMethod? PayoutMethod,
    decimal? DueReductionOverrideAmount,
    string? DueOverrideReason,
    string? Notes,
    IReadOnlyList<RecordSaleReturnItemCommand> Items);

public sealed record RecordSaleReturnItemCommand(
    Guid SaleItemId,
    decimal Quantity,
    SaleReturnCondition Condition,
    decimal? ApprovedRefundAmount,
    string? Notes);
