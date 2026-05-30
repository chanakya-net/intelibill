namespace Intelibill.Application.Features.Sales.Commands.ReserveInvoiceLease;

public sealed record ReserveInvoiceLeaseCommand(
    Guid ActorUserId,
    Guid ShopId,
    string DeviceId,
    int? BlockSize);
