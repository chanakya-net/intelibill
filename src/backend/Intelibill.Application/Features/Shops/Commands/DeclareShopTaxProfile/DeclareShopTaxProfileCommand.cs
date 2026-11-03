using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Shops.Commands.DeclareShopTaxProfile;

/// <summary>The GST registration a shop issues documents under, as its owner declares it.</summary>
public sealed record ShopTaxProfileInput(
    string? LegalName,
    string? StateCode,
    TaxRegime TaxRegime,
    string? InvoiceSeriesCode,
    EInvoiceStatus? EInvoiceStatus);

public sealed record DeclareShopTaxProfileCommand(
    Guid UserId,
    Guid ShopId,
    ShopTaxProfileInput Profile,
    string? GstNumber);
