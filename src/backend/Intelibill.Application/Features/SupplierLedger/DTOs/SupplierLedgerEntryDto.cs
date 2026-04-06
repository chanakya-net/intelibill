using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.SupplierLedger.DTOs;

public sealed record SupplierLedgerEntryDto(
    Guid Id,
    Guid SupplierId,
    SupplierLedgerEntryType EntryType,
    decimal Amount,
    DateOnly EntryDate,
    string? Notes);
