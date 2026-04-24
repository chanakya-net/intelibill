using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Customers.DTOs;

public sealed record CustomerLedgerEntryDto(
    Guid EntryId,
    Guid? SaleId,
    CustomerLedgerEntryType EntryType,
    decimal Amount,
    DateOnly EntryDate,
    string? Notes,
    decimal RunningBalance);
