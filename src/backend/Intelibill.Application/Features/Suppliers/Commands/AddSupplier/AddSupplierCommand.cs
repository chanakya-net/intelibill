namespace Intelibill.Application.Features.Suppliers.Commands.AddSupplier;

public sealed record AddSupplierCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    string Name,
    string? ContactPersonName,
    string? ContactPersonPhone,
    string Address,
    string City,
    string State,
    string Pin,
    bool IsActive,
    bool IsPreferred);