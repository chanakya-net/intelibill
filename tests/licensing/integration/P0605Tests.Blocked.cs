using System.Net.Http.Json;
using Intelibill.Domain.Enums;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0605Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IncomingActivationRechecksConsentAuthorityAndMemberCapacity(bool changedOwner)
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var (id, _) = await ConsentAsync(f, p, term);
        var (quote, paid) = await RenewAsync(p, term, "Month");
        var successor = paid.GetProperty("termId").GetGuid();
        var shop = p.Shops[3];
        if (changedOwner)
            await LicensingPostgreSqlFixture.ExecuteAsync(f.Checkout.Delivery.MerchantDb,
                $"UPDATE shop_memberships SET authority_version=authority_version+1 WHERE user_id='{shop.UserId}' AND shop_id='{shop.ShopId}'");
        else
            for (var i = 0; i < 5; i++) await P0209Fixture.SignInAsync(f.Hosts, ShopRole.Staff, shop.ShopId);
        await f.Checkout.Delivery.DrainAsync(true);
        await f.Checkout.Delivery.DrainAsync(false);
        f.At(February29);
        await f.Checkout.Delivery.ScanAsync(true);
        await f.Checkout.Delivery.DrainAsync(true);
        await f.Checkout.Delivery.DrainAsync(false);
        var preview = await p.Payer.Client.GetFromJsonAsync<System.Text.Json.JsonElement>(PathFor(p, term));
        Assert.Equal("NeedsReview", Assert.Single(preview.GetProperty("replacements").EnumerateArray()).GetProperty("activation").GetString());
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM coverage_replacement_activations WHERE replacement_id='{id}'"));
        Assert.Equal(0L, await f.Checkout.Delivery.MerchantScalarAsync($"SELECT count(*) FROM shop_license_states WHERE shop_id='{shop.ShopId}' AND term_id='{successor}'"));
        var payment = await P0302Fixture.StatusAsync(p.Payer.Client, quote);
        Assert.Equal("NeedsReview", payment.GetProperty("access").GetString());
    }

    [Fact]
    public async Task RevocationAfterCheckoutStartedPreservesCapturedFundsForReview()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var (_, path) = await ConsentAsync(f, p, term);
        var quote = await AcceptAsync(p, await RenewalQuoteAsync(p, term, "Month"));
        var attempt = await StartPaymentAsync(p, quote);
        using var revoke = await P0604Tests.Decide(p.Shops[3].Client, path, "revoke");
        revoke.EnsureSuccessStatusCode();
        await f.Checkout.Delivery.DrainAsync(false);
        await P0302Fixture.CaptureAsync(p.Payer.Client, quote, attempt);
        var paid = await P0302Fixture.VerifyAsync(p.Payer.Client, quote, attempt);
        P0302Fixture.AssertStatus(paid,"Verified","NeedsReview","NotApplicable");
        Assert.Equal(2L, await f.CountAsync("verified_payments"));
        Assert.Equal(1L, await f.CountAsync("payment_fulfilments"));
    }
}
