using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.DTOs;

public sealed record InvoiceLeaseLicenceEvidenceDto(
    int Version,
    Guid GrantId,
    string GrantSha256,
    string KeyId);

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
    DateTimeOffset ExpiresAt,
    InvoiceSellerProfile? SellerProfile = null,
    InvoiceLeaseLicenceEvidenceDto? LicenceEvidence = null);
