namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record InvoiceLeaseDto(
    Guid LeaseId,
    Guid ShopId,
    string DeviceId,
    string FiscalYear,
    string Prefix,
    int NumberPadding,
    int RangeStart,
    int RangeEnd,
    int NextNumber,
    int RemainingCount,
    DateTimeOffset ReservedAt,
    DateTimeOffset ExpiresAt);
