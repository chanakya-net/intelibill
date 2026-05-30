namespace Intelibill.Application.Features.Suppliers.Commands.DeleteSupplier;

public sealed record DeleteSupplierCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid SupplierId);
