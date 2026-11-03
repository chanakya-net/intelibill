namespace Intelibill.Application.Features.Sales.Commands.ReserveInvoiceLease;

public sealed record ReserveInvoiceLeaseCommand(
    Guid ActorUserId,
    Guid ShopId,
    string DeviceId,
    int? BlockSize,
    // The sign-in the range is reserved from; recorded as the lease's provenance.
    Guid? SessionId = null);
