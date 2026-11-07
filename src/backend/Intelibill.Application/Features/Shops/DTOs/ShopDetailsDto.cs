using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Shops.DTOs;

public sealed record ShopDetailsDto(
    Guid ShopId,
    string Name,
    string Address,
    string City,
    string State,
    string Pincode,
    string? ContactPerson,
    string? MobileNumber,
    string? GstNumber,
    string? BankName = null,
    string? BankAccountNumber = null,
    string? BankAccountType = null,
    string? IfscCode = null,
    string? AccountHolderName = null)
{
    public ShopTaxProfileDto TaxProfile { get; init; } = ShopTaxProfileDto.Undeclared;

    /// <summary>The IANA zone the shop reads its business dates in. Read-only.</summary>
    public string BusinessTimeZoneId { get; init; } = Domain.Common.BusinessTimeZones.DefaultId;
}

public sealed record ShopTaxProfileDto(
    string? LegalName,
    string? StateCode,
    TaxRegime TaxRegime,
    string InvoiceSeriesCode,
    EInvoiceStatus EInvoiceStatus,
    DateTimeOffset? EInvoiceAttestedAt)
{
    internal static ShopTaxProfileDto Undeclared { get; } =
        new(null, null, TaxRegime.NotDeclared, Domain.Common.InvoiceNumberFormat.DefaultSeriesCode, EInvoiceStatus.NotAttested, null);

    public static ShopTaxProfileDto From(Shop shop) =>
        new(shop.LegalName, shop.StateCode, shop.TaxRegime, shop.InvoiceSeriesCode, shop.EInvoiceStatus, shop.EInvoiceAttestedAt);
}
