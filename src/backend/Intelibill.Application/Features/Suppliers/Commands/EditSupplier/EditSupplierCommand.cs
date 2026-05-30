namespace Intelibill.Application.Features.Suppliers.Commands.EditSupplier;

using Intelibill.Domain.Enums;

public sealed record EditSupplierCommand(
    Guid ActorUserId,
    Guid ActiveShopId,
    Guid SupplierId,
    string Name,
    string? ContactPersonName,
    string? ContactPersonPhone,
    string Address,
    string City,
    string State,
    string Pin,
    bool IsActive,
    bool IsPreferred);
