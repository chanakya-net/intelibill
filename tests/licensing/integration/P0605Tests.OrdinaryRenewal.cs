using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0605Tests
{
    [Fact]
    public async Task PaidCapacityIncreaseBlocksTheScheduledReplacementAndRenewsTheOriginalShops()
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var (id, _) = await ConsentAsync(f, p, term);
        var unchanged = await RenewalQuoteAsync(p, term, "Month");
        Assert.Equal(id, unchanged.Replacement?.ReplacementId);
        await AssertDifferentAllowancesExcludedAsync(f, p, term, unchanged);
        var increase = await P0602Tests.IncreaseSeatsAsync(f, p, term, February29);
        await PayAsync(p, increase);
        var delivery = f.Checkout.Delivery;
        for (var round = 0; round < 2; round++)
        {
            await delivery.DrainAsync(true);
            await delivery.DrainAsync(false);
        }
        var preview = await p.Payer.Client.GetFromJsonAsync<CoverageReplacementPreviewV1>(PathFor(p, term));
        Assert.Equal(4, preview!.Capacity);
        Assert.Equal("Blocked", Assert.Single(preview.Replacements).State);
        var (quote, paid) = await RenewAsync(p with { Plan = increase.PlanVersionId }, term, "Month");
        await AssertOrdinarySuccessorAsync(f, p, quote, paid);
    }

    [Fact]
    public async Task AssigningAnUnusedSlotChangesTheSourceHashWithoutChangingCapacity()
    {
        await using var f = await StartAsync(database, January31);
        var p = await f.PackageAsync(4, ["Central", "East", "North", "South"]);
        var (_, term) = await PurchaseAsync(p with { Shops = [p.Shops[0], p.Shops[2]] }, "Month");
        await f.Checkout.Delivery.DrainAsync(true);
        await f.Checkout.Delivery.DrainAsync(false);
        f.At(February14);
        await ConsentAsync(f, p, term);
        var assignment = await P0604Tests.AcceptSlotAsync(p, term, p.Shops[1]);
        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(1L, await f.CountAsync("coverage_slot_assignments", $"assignment_id='{assignment}' AND state='Applied'"));
        var (quote, paid) = await RenewAsync(p, term, "Month");
        await AssertOrdinarySuccessorAsync(f, p, quote, paid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateConsentCannotRedirectAnOrdinaryRenewal(bool alreadyFulfilled)
    {
        await using var f = await StartAsync(database, January31);
        var (p, term) = await PrepareAsync(f);
        var (id, _) = await QueueConsentAsync(p, term);
        var quote = await AcceptAsync(p, await RenewalQuoteAsync(p, term, "Month"));
        var attempt = await StartPaymentAsync(p, quote);
        if (alreadyFulfilled)
        {
            await P0302Fixture.CaptureAsync(p.Payer.Client, quote, attempt);
            var fulfilled = await P0302Fixture.VerifyAsync(p.Payer.Client, quote, attempt);
            await AssertOrdinarySuccessorAsync(f, p, quote, fulfilled);
        }

        await f.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(1L, await f.CountAsync("coverage_replacements",
            $"replacement_id='{id}' AND consent_event IS NOT NULL AND state='Blocked' AND reason='SourceChanged'"));
        if (!alreadyFulfilled) await P0302Fixture.CaptureAsync(p.Payer.Client, quote, attempt);
        var paid = await P0302Fixture.VerifyAsync(p.Payer.Client, quote, attempt);
        await AssertOrdinarySuccessorAsync(f, p, quote, paid);
    }

    // Isolate the offered capacity and member allowance while the purchased source hash still matches.
    private static async Task AssertDifferentAllowancesExcludedAsync(P0308Fixture f, Package p, Guid term, CheckoutQuoteV1 current)
    {
        await using var scope = f.Hosts.Licensing.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicensingDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"""
            SELECT set_config('app.billing_account_id',{p.Account.ToString("D")},true),
                set_config('app.billing_actor_id',{p.Payer.UserId.ToString("D")},true)
            """);
        var store = scope.ServiceProvider.GetRequiredService<ICheckoutOrderStore>();
        Task<CoverageReplacementOrderV1?> Candidate(int capacity, int seats) =>
            store.ReplacementAsync(p.Account, term, capacity, seats, February29, February14, CancellationToken.None);
        Assert.Equal(current.Replacement, await Candidate(current.Capacity, current.MembersPerShop));
        Assert.Null(await Candidate(current.Capacity + 1, current.MembersPerShop));
        Assert.Null(await Candidate(current.Capacity, current.MembersPerShop + 1));
    }

    private static async Task AssertOrdinarySuccessorAsync(P0308Fixture f, Package p, CheckoutQuoteV1 quote, JsonElement paid)
    {
        Assert.Null(quote.Replacement);
        Assert.Equal(CheckoutOrdersV1.ShopHash(p.Shops[..3].Select(s => s.ShopId)), quote.ShopHash);
        Assert.Equal("Fulfilled", paid.GetProperty("fulfilment").GetString());
        var term = paid.GetProperty("termId").GetGuid();
        Assert.Equal(1L, await f.CountAsync("payment_fulfilments", $"order_id='{quote.OrderId}' AND term_id='{term}'"));
        Assert.Equal(3L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{term}'"));
        foreach (var shop in p.Shops[..3])
            Assert.Equal(1L, await f.CountAsync("payment_fulfilment_shops", $"term_id='{term}' AND shop_id='{shop.ShopId}' AND replacement_id IS NULL"));
    }
}
