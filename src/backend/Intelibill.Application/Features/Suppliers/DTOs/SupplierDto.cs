namespace Intelibill.Application.Features.Suppliers.DTOs;

using Intelibill.Domain.Enums;

public sealed record SupplierDto(
    Guid SupplierId,
    string Name,
    string? ContactPersonName,
    string? ContactPersonPhone,
    string? Address,
    string? City,
    string? State,
    string? Pin,
    bool IsSystem,
    bool IsActive,
    bool IsPreferred,
    decimal BalanceDue);
