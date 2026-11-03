using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class ShopTaxProfileTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    private static Shop NewShop() => Shop.Create("Main", "1 Road", "Bengaluru", "Karnataka", "560001", null, null, null);

    [Fact]
    public void New_shops_have_not_declared_a_regime()
    {
        var shop = NewShop();

        Assert.Equal(TaxRegime.NotDeclared, shop.TaxRegime);
        Assert.Equal("A", shop.InvoiceSeriesCode);
        Assert.Equal($"shop:{shop.Id:N}", shop.SellerRegistrationKey);
    }

    [Fact]
    public void Regular_profile_records_gstin_series_and_attestation()
    {
        var shop = NewShop();

        var result = shop.DeclareTaxProfile(" Main Pvt Ltd ", "29", TaxRegime.Regular, "29aapfu0939f1zr", "b", EInvoiceStatus.BelowThreshold, Actor, Now);

        Assert.False(result.IsError);
        Assert.Equal("Main Pvt Ltd", shop.LegalName);
        Assert.Equal("29AAPFU0939F1ZR", shop.GstNumber);
        Assert.Equal("B", shop.InvoiceSeriesCode);
        Assert.Equal(EInvoiceStatus.BelowThreshold, shop.EInvoiceStatus);
        Assert.Equal(Now, shop.EInvoiceAttestedAt);
        Assert.Equal(Actor, shop.EInvoiceAttestedByUserId);
        Assert.Equal("29AAPFU0939F1ZR", shop.SellerRegistrationKey);
    }

    [Theory]
    [InlineData(TaxRegime.NotDeclared, "29", "29AAPFU0939F1ZR", EInvoiceStatus.BelowThreshold, "Shop.TaxRegimeRequired")]
    [InlineData(TaxRegime.Regular, "99", "29AAPFU0939F1ZR", EInvoiceStatus.BelowThreshold, "Shop.StateCodeInvalid")]
    [InlineData(TaxRegime.Regular, "29", null, EInvoiceStatus.BelowThreshold, "Shop.GstinRequiredForRegime")]
    [InlineData(TaxRegime.Composition, "29", "29AAPFU0939F1ZX", EInvoiceStatus.NotAttested, "Shop.GstinInvalid")]
    [InlineData(TaxRegime.Regular, "27", "29AAPFU0939F1ZR", EInvoiceStatus.BelowThreshold, "Shop.GstinStateMismatch")]
    [InlineData(TaxRegime.Unregistered, "29", "29AAPFU0939F1ZR", EInvoiceStatus.NotAttested, "Shop.GstinNotAllowedForUnregistered")]
    [InlineData(TaxRegime.Regular, "29", "29AAPFU0939F1ZR", EInvoiceStatus.NotAttested, "Shop.EInvoiceAttestationRequired")]
    public void Inconsistent_profiles_are_refused(TaxRegime regime, string state, string? gstin, EInvoiceStatus attestation, string code)
    {
        var shop = NewShop();

        var result = shop.DeclareTaxProfile(null, state, regime, gstin, null, attestation, Actor, Now);

        Assert.Equal(code, result.FirstError.Code);
        Assert.Equal(TaxRegime.NotDeclared, shop.TaxRegime);
    }

    [Fact]
    public void Invalid_series_code_is_refused()
    {
        var result = NewShop().DeclareTaxProfile(null, "29", TaxRegime.Unregistered, null, "TOO-LONG", EInvoiceStatus.NotAttested, Actor, Now);

        Assert.Equal("Shop.InvoiceSeriesCodeInvalid", result.FirstError.Code);
    }

    [Fact]
    public void Composition_and_unregistered_clear_the_attestation()
    {
        var shop = NewShop();
        shop.DeclareTaxProfile(null, "29", TaxRegime.Regular, "29AAPFU0939F1ZR", null, EInvoiceStatus.Exempt, Actor, Now);

        var result = shop.DeclareTaxProfile(null, "29", TaxRegime.Composition, "29AAPFU0939F1ZR", null, EInvoiceStatus.Exempt, Actor, Now);

        Assert.False(result.IsError);
        Assert.Equal(EInvoiceStatus.NotAttested, shop.EInvoiceStatus);
        Assert.Null(shop.EInvoiceAttestedAt);
    }

    [Fact]
    public void Declared_gstin_cannot_change_through_shop_details()
    {
        var shop = NewShop();
        Assert.False(shop.DeclareTaxProfile(null, "29", TaxRegime.Regular, "29AAPFU0939F1ZR", null, EInvoiceStatus.BelowThreshold, Actor, Now).IsError);

        var changed = shop.UpdateDetails("Renamed", "1 Road", "Bengaluru", "Karnataka", "560001", null, null, "29AABCS1234C1ZT");

        Assert.Equal("Shop.GstinManagedByTaxProfile", changed.FirstError.Code);
        Assert.Equal("Main", shop.Name);
        Assert.Equal("29AAPFU0939F1ZR", shop.GstNumber);

        Assert.False(shop.UpdateDetails("Renamed", "1 Road", "Bengaluru", "Karnataka", "560001", null, null, " 29aapfu0939f1zr ").IsError);
        Assert.False(shop.UpdateDetails("Renamed", "1 Road", "Bengaluru", "Karnataka", "560001", null, null, null).IsError);
        Assert.Equal("Renamed", shop.Name);
        Assert.Equal("29AAPFU0939F1ZR", shop.GstNumber);
    }

    [Fact]
    public void Undeclared_shop_details_still_set_the_gstin()
    {
        var shop = NewShop();

        Assert.False(shop.UpdateDetails("Main", "1 Road", "Bengaluru", "Karnataka", "560001", null, null, "29AAPFU0939F1ZR").IsError);
        Assert.Equal("29AAPFU0939F1ZR", shop.GstNumber);
    }
}
