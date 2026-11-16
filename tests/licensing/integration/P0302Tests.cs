using System.Net;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed partial class P0302Tests(LicensingPostgreSqlFixture database) : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task SuccessfulLocalCaptureActivatesWholePackageOnceAndAppliesAfterAcknowledgement()
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var (payer, account, shops, quote) = await fixture.AcceptedQuoteAsync(3);

        var before = await StatusAsync(payer.Client, quote);
        AssertStatus(before, "NotStarted", "NotFulfilled", "NotApplicable");
        Assert.True(before.GetProperty("canStart").GetBoolean());
        Assert.Equal(quote.AmountPaise, before.GetProperty("amountPaise").GetInt64());

        var started = await P0302Fixture.StartAsync(payer.Client, quote, HttpStatusCode.Created);
        AssertStatus(started, "AwaitingPayment", "NotFulfilled", "NotApplicable");
        Assert.Equal("development-simulator", started.GetProperty("launch").GetProperty("flow").GetString());
        var attempt = started.GetProperty("attemptId").GetGuid();

        var captured = await CaptureAsync(payer.Client, quote, attempt);
        AssertStatus(captured, "AwaitingPayment", "NotFulfilled", "NotApplicable");
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));

        var verified = await VerifyAsync(payer.Client, quote, attempt);
        AssertStatus(verified, "Verified", "Fulfilled", "Applying");
        Assert.Equal(3, verified.GetProperty("shopsTotal").GetInt32());
        Assert.Equal(0, verified.GetProperty("shopsApplied").GetInt32());
        Assert.Equal(quote.PreviewStartsAt, verified.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(quote.PreviewEndsAt, verified.GetProperty("termEndsAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, verified.GetProperty("launch").ValueKind);

        var again = await VerifyAsync(payer.Client, quote, attempt);
        AssertStatus(again, "Verified", "Fulfilled", "Applying");
        Assert.Equal(verified.GetProperty("termId").GetGuid(), again.GetProperty("termId").GetGuid());
        await AssertSingleFulfilmentAsync(fixture, quote, 3);

        var shop = await ReadAsync(await ShopStatusAsync(payer.Client, payer.ShopId));
        Assert.Equal("Active", shop.GetProperty("state").GetString());
        Assert.Equal("Paid", shop.GetProperty("term").GetProperty("basis").GetString());
        Assert.Equal(verified.GetProperty("termId").GetGuid(), shop.GetProperty("term").GetProperty("termId").GetGuid());

        await fixture.Delivery.DrainAsync(source: true);
        AssertStatus(await StatusAsync(payer.Client, quote), "Verified", "Fulfilled", "Applying");
        Assert.True(await fixture.Delivery.DeliverAsync(source: false));
        var partial = await StatusAsync(payer.Client, quote);
        AssertStatus(partial, "Verified", "Fulfilled", "Applying");
        Assert.Equal(1, partial.GetProperty("shopsApplied").GetInt32());
        await fixture.Delivery.DrainAsync(source: false);
        var applied = await StatusAsync(payer.Client, quote);
        AssertStatus(applied, "Verified", "Fulfilled", "Applied");
        Assert.Equal(3, applied.GetProperty("shopsApplied").GetInt32());
        Assert.All(shops, shop => Assert.NotEqual(Guid.Empty, shop.ShopId));
        Assert.Equal(account, applied.GetProperty("accountId").GetGuid());
    }

    internal static async Task AssertSingleFulfilmentAsync(P0302Fixture fixture, Intelibill.Shared.Contracts.V1.CheckoutQuoteV1 quote,
        int shops)
    {
        var order = $"order_id = '{quote.OrderId:D}'";
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", order));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments", order));
        Assert.Equal((long)shops, await fixture.CountAsync("payment_fulfilment_shops", order));
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms",
            $"term_id IN (SELECT term_id FROM public.payment_fulfilments WHERE {order})"));
        Assert.Equal(1L, await fixture.CountAsync("subscription_term_facts",
            $"basis = 'Paid' AND price_paise = {quote.AmountPaise} AND plan_version_id = '{quote.PlanVersionId:D}'"));
    }
}
