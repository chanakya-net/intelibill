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
    public ShopSetupAccessDto? SetupAccess { get; init; }

    internal static ShopDetailsDto From(Shop shop, BankAccount? account, ShopSetupAccessDto setupAccess) =>
        new(shop.Id, shop.Name, shop.Address, shop.City, shop.State, shop.Pincode, shop.ContactPerson,
            shop.MobileNumber, shop.GstNumber, account?.BankName, account?.AccountNumber, account?.AccountType?.ToString(),
            account?.IfscCode, account?.AccountHolderName)
        {
            TaxProfile = ShopTaxProfileDto.From(shop),
            BusinessTimeZoneId = shop.BusinessTimeZoneId,
            SetupAccess = setupAccess,
        };

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


public sealed record ShopSetupAccessDto(Guid ShopId, int ContractVersion, bool Enforced, string Status,
    IReadOnlyList<string> WritableProfileFields, bool CanPrepareTax, bool CanSavePreferences, bool CanEditOperatingSettings,
    bool HasOperatingAccess, bool CanCreateAdditionalShop);
