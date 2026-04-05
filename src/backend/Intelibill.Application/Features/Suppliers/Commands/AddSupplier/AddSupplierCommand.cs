namespace Intelibill.Application.Features.Suppliers.Commands.AddSupplier;

using Intelibill.Domain.Enums;

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
    decimal Amount,
    SupplierStatus Status,
    bool IsActive,
    bool IsPreferred);
