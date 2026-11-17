using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Licensing.Application.Payments;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0309Tests
{
    [Fact]
    public async Task MixedPackageKeepsTrialUntilTheRoundedPaidStartAndNeedsItsSignedDelivery()
    {
        await using var fixture = await StartAsync(database, March31);
        var package = await fixture.PackageAsync(2);
        await ApplyTrialAsync(fixture, package.Payer);
        var original = await P0211Tests.OriginalFactsAsync(fixture.Checkout.Trial, package.Payer.ShopId);
        fixture.At(March31.AddDays(3));
        var quote = await AcceptAsync(package, await FirstQuoteAsync(package, "Year"));
        Assert.Equal(1, quote.FreshStart?.TrialShopCount);
        var attempt = await StartPaymentAsync(package, quote);
        await P0302Fixture.CaptureAsync(package.Payer.Client, quote, attempt);

        var ready = March31.AddDays(4).AddTicks(10);
        fixture.At(ready);
        var paid = await P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt);
        var start = March31.AddDays(4).AddSeconds(1);
        Assert.Equal(start, paid.GetProperty("termStartsAt").GetDateTimeOffset());
        Assert.Equal(start.AddYears(1), paid.GetProperty("termEndsAt").GetDateTimeOffset());
        Assert.Equal("Trial", (await ShopTermAsync(package, package.Payer)).GetProperty("basis").GetString());
        Assert.Equal("Trial", (await fixture.Checkout.Delivery.LatestSnapshotAsync(package.Payer.ShopId)).Shop!.Basis);
        await DrainAsync(fixture);
        P0302Fixture.AssertStatus(await P0302Fixture.StatusAsync(package.Payer.Client, quote), "Verified", "Fulfilled", "Applying");

        fixture.At(start);
        await fixture.Checkout.Delivery.ScanAsync(source: true);
        await DrainAsync(fixture);
        P0302Fixture.AssertStatus(await P0302Fixture.StatusAsync(package.Payer.Client, quote), "Verified", "Fulfilled", "Applied");
        Assert.Equal("Paid", (await ShopTermAsync(package, package.Payer)).GetProperty("basis").GetString());
        Assert.Equal(original, await P0211Tests.OriginalFactsAsync(fixture.Checkout.Trial, package.Payer.ShopId));
        Assert.Equal(1L, await fixture.CountAsync("payment_trial_conversions"));
        Assert.Equal(2L, await fixture.CountAsync("payment_fulfilment_shops", $"order_id = '{quote.OrderId:D}'"));
    }

    [Fact]
    public async Task UpgradeRetainsAcceptedV1OrderAndRejectsEvidenceLossAfterItsFulfilment()
    {
        const string baseline = "20261117120000_PaidRenewals";
        await using var fixture = await StartAsync(database, March31);
        var package = await fixture.PackageAsync(1);
        var pending = await FirstQuoteAsync(package, "Month");
        var legacy = await P0309Fixture.RepresentLegacyQuoteAsync(fixture.Hosts.LicensingOwner, pending);
        var quote = await AcceptAsync(package, legacy);
        var attempt = await StartPaymentAsync(package, quote);
        await P0302Fixture.CaptureAsync(package.Payer.Client, quote, attempt);
        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        var migration = context.GetService<IMigrator>();
        await migration.MigrateAsync(baseline);
        await migration.MigrateAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant);
        var reloaded = await package.Payer.Client.GetFromJsonAsync<CheckoutQuoteV1>(
            $"{P0301Tests.OrdersPath(package.Account)}/{quote.OrderId:D}");
        Assert.Equal(quote, reloaded);
        var paid = await P0302Fixture.VerifyAsync(package.Payer.Client, quote, attempt);
        P0302Fixture.AssertStatus(paid, "Verified", "Fulfilled", "Applying");
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => migration.MigrateAsync(baseline));
        Assert.Equal("55000", Assert.IsType<PostgresException>(refused.InnerException).SqlState);
        Assert.Equal(1L, await fixture.CountAsync("paid_fresh_starts"));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Checkout.Trial.Catalogue.Runtime,
            "SELECT count(*) FROM public.paid_fresh_starts"));
    }

    [Fact]
    public void FreshStartSourceHashMatchesCanonicalCorpus()
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectPaths.Root,
            "tests/contracts/licensing/paid-fresh-start-v1.json")));
        Assert.Equal(CheckoutOrdersV1.FreshStartPolicy, corpus.RootElement.GetProperty("policy").GetString());
        var examples = corpus.RootElement.GetProperty("trialSourceHashes").EnumerateArray().ToArray();
        var empty = new CheckoutSource([], null);
        var trial = new CheckoutTrialSource(Guid.Parse("00000000-0000-4000-8000-000000000001"),
            Guid.Parse("00000000-0000-4000-8000-000000000002"), 2, Utc("2027-04-17T04:30:00.123456Z"));
        var frozen = new CheckoutSource([], null, [trial]);
        Assert.Equal(examples[0].GetProperty("sha256").GetString(), empty.FreshStart().TrialSourceHash);
        Assert.Equal(examples[1].GetProperty("sha256").GetString(), frozen.FreshStart().TrialSourceHash);
        Assert.NotEqual(frozen.FreshStart(), (frozen with { Trials = [trial with { Revision = 3 }] }).FreshStart());
    }

    internal static async Task ApplyTrialAsync(P0308Fixture fixture, P0209Fixture.Merchant owner)
    {
        var trial = fixture.Checkout.Trial;
        using var licensing = fixture.Hosts.Licensing.CreateClient();
        var started = fixture.Hosts.StatusClock.Now;
        using var request = trial.Delivery(new ShopTrialConsentV1(Guid.NewGuid(), owner.ShopId, owner.UserId,
            Guid.NewGuid(), 0, 1, started, trial.Accept()));
        using var response = await licensing.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Applied", (await response.Content.ReadFromJsonAsync<ShopTrialDeliveryResultV1>())!.Status);
    }

    internal static async Task DrainAsync(P0308Fixture fixture)
    {
        await fixture.Checkout.Delivery.DrainAsync(source: true);
        await fixture.Checkout.Delivery.DrainAsync(source: false);
    }
}
