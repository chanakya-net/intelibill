namespace Intelibill.Application.Features.Items.Commands.AddItem;

public sealed record AddItemCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    string Name,
    string Barcode,
    string? Description,
    string Uom,
    bool IsActive);