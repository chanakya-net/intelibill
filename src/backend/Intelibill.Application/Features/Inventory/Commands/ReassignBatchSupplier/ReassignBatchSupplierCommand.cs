using Intelibill.Application.Common.Security;
namespace Intelibill.Application.Features.Inventory.Commands.ReassignBatchSupplier;

public sealed record ReassignBatchSupplierCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid BatchId,
    Guid NewSupplierId,
    SessionClaims? Session);
