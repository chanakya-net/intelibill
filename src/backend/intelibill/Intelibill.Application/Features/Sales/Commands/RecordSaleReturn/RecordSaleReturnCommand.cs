using Intelibill.Application.Features.Tills;
using Intelibill.Domain.ValueObjects;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;

public sealed record RecordSaleReturnCommand(
    Guid ActorUserId,
    Guid ShopId,
    Guid SaleId,
    ReturnPayoutDestination? PayoutDestination,
    decimal? DueReductionOverrideAmount,
    string? DueOverrideReason,
    string? Notes,
    DateTimeOffset? CreditNoteExpiresAt,
    string? CreditNoteReason,
    IReadOnlyList<RecordSaleReturnItemCommand> Items,
    string? OperationKey = null, TillPostingContext? Till = null, TenderLine? RefundTender = null, bool? RefundConfirmed = null);

public sealed record RecordSaleReturnItemCommand(
    Guid SaleItemId,
    decimal Quantity,
    SaleLineType LineType,
    SaleReturnCondition? Condition,
    decimal? ApprovedRefundAmount,
    string? Notes);
