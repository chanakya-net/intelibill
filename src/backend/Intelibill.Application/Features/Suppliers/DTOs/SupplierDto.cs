namespace Intelibill.Application.Features.Suppliers.DTOs;

public sealed record SupplierDto(
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