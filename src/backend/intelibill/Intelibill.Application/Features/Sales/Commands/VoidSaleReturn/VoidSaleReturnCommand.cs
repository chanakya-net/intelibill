using Intelibill.Application.Features.Tills;
using Intelibill.Domain.ValueObjects;
namespace Intelibill.Application.Features.Sales.Commands.VoidSaleReturn;

public sealed record VoidSaleReturnCommand(
    Guid ActorUserId,
    Guid ShopId,
    Guid SaleReturnId,
    string Reason, string? OperationKey = null, TillPostingContext? Till = null, TillSettlementEvidence? SettlementEvidence = null);
