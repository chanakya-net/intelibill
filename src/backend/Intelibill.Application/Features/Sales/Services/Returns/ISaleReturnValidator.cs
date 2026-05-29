using ErrorOr;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Services.Returns;

public sealed record SaleReturnValidationRequest(
    Guid ActorUserId,
    Guid ShopId,
    Guid SaleId,
    decimal? DueReductionOverrideAmount,
    string? DueOverrideReason,
    IReadOnlyList<SaleReturnValidationLineRequest> Items);

public sealed record SaleReturnValidationLineRequest(
    Guid SaleItemId,
    decimal Quantity,
    SaleLineType LineType,
    SaleReturnCondition Condition,
    decimal? ApprovedRefundAmount,
    string? Notes);

public sealed record SaleReturnValidationResult(
    User Actor,
    Shop Shop,
    ShopMembership Membership,
    Sale Sale,
    IReadOnlyList<SaleReturn> ActiveReturns,
    IReadOnlyList<ValidatedSaleReturnLine> Lines,
    SaleReturnCalculationResult Calculation);

public sealed record ValidatedSaleReturnLine(
    SaleReturnValidationLineRequest Request,
    SaleItem SaleItem,
    InventoryBatch? Batch,
    decimal ReturnedQuantity,
    decimal ReturnableQuantity);

public interface ISaleReturnValidator
{
    Task<ErrorOr<SaleReturnValidationResult>> ValidateAsync(
        SaleReturnValidationRequest request,
        CancellationToken cancellationToken);
}
