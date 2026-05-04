namespace Intelibill.Application.Features.Sales.Commands.VoidSaleReturn;

public sealed record VoidSaleReturnCommand(
    Guid ActorUserId,
    Guid ShopId,
    Guid SaleReturnId,
    string Reason);
