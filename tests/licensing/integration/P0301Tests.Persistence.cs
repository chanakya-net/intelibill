using System.Net;
using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0301Tests
{
    [Fact]
    public async Task ConcurrentAcceptancesFreezeOneCompletePackage()
    {
        await using var fixture = await StartCheckoutAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var (payer, account, command) = await PrepareAsync(fixture);
        var creations = await Task.WhenAll(payer.Client.PostAsJsonAsync(OrdersPath(account), command),
            payer.Client.PostAsJsonAsync(OrdersPath(account), command));
        using var created = Assert.Single(creations, r => r.StatusCode == HttpStatusCode.Created);
        using var same = Assert.Single(creations, r => r.StatusCode == HttpStatusCode.OK);
        var quote = (await created.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!;
        Assert.Equal(quote, await same.Content.ReadFromJsonAsync<CheckoutQuoteV1>());
        var outcomes = await Task.WhenAll(payer.Client.PostAsJsonAsync(AcceptPath(quote), Accept(quote)),
            payer.Client.PostAsJsonAsync(AcceptPath(quote), Accept(quote)));
        using var accepted = Assert.Single(outcomes, r => r.StatusCode == HttpStatusCode.OK);
        using var refused = Assert.Single(outcomes, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(CheckoutOrdersV1.AlreadyAccepted, await P0206Fixture.CodeAsync(refused));
        Assert.Equal(1L, await fixture.CountAsync("checkout_order_acceptances"));
        Assert.Equal(1L, await fixture.CountAsync("checkout_order_shops"));

        var outsider = await SignInAsync(fixture.Hosts);
        using var forbidden = await outsider.Client.GetAsync($"{OrdersPath(account)}/{quote.OrderId:D}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var receipt = (await accepted.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!.Acceptance!;
        using var recovered = await payer.Client.GetAsync($"{OrdersPath(account)}/operations/accept/{receipt.OperationKey:D}");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(receipt, (await recovered.Content.ReadFromJsonAsync<CheckoutQuoteV1>())!.Acceptance);
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Catalogue.Runtime,
            "SELECT count(*) FROM public.checkout_orders"));
        await AssertRetainedAsync(fixture, quote);
    }

    [Fact]
    public async Task MigrationKeepsPopulatedPredecessorAndRefusesDowngradeAfterQuote()
    {
        await using var fixture = await StartCheckoutAsync(database, new P0201Clock { Now = P0210Fixture.Start });
        var (payer, account, command) = await PrepareAsync(fixture);
        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        var migrator = context.GetService<IMigrator>();
        const string predecessor = "20261116220000_AddSubscriptionCancellationWithdrawals";
        await migrator.MigrateAsync(predecessor);
        var before = await ExistingFactsAsync(fixture);
        Assert.NotNull(before);
        await migrator.MigrateAsync();
        await fixture.Catalogue.ExecuteAsync(P0302Fixture.RuntimeGrant);
        Assert.Equal(before, await ExistingFactsAsync(fixture));
        var quote = await CreateQuoteAsync(payer.Client, account, command);
        quote = await P0309Fixture.RepresentLegacyQuoteAsync(fixture.LicensingOwner, quote);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => migrator.MigrateAsync(predecessor));
        Assert.Equal("55000", Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        Assert.Equal(quote, await payer.Client.GetFromJsonAsync<CheckoutQuoteV1>($"{OrdersPath(account)}/{quote.OrderId:D}"));
    }

    private static Task<object?> ExistingFactsAsync(P0210Fixture fixture) =>
        LicensingPostgreSqlFixture.ReadScalarAsync(fixture.LicensingOwner, """
            SELECT jsonb_build_array(
                (SELECT jsonb_agg(to_jsonb(m) ORDER BY id) FROM billing_account_members m),
                (SELECT jsonb_agg(to_jsonb(a) ORDER BY shop_id) FROM shop_affiliations a),
                (SELECT jsonb_agg(to_jsonb(c) ORDER BY version_id) FROM published_catalogue_offers c)
            )::text
            """);

    private static async Task AssertRetainedAsync(P0210Fixture fixture, CheckoutQuoteV1 quote)
    {
        foreach (var sql in new[]
        {
            "UPDATE checkout_orders SET amount_paise = 1",
            "DELETE FROM checkout_order_shops",
            // CASCADE: billing_document_versions references acceptances, and without it PostgreSQL refuses the
            // statement on that foreign key (0A000) before the retention trigger can.
            "TRUNCATE checkout_order_acceptances CASCADE",
        })
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                LicensingPostgreSqlFixture.ExecuteAsync(fixture.LicensingOwner, sql));
            Assert.Equal("55000", failure.SqlState);
        }
        var lateShop = await Assert.ThrowsAsync<PostgresException>(() =>
            LicensingPostgreSqlFixture.ExecuteAsync(fixture.LicensingOwner, $"""
                INSERT INTO checkout_order_shops (account_id, order_id, shop_id, shop_name, affiliation_request_id, replay_id)
                VALUES ('{quote.AccountId:D}', '{quote.OrderId:D}', gen_random_uuid(), 'Late shop', gen_random_uuid(), gen_random_uuid())
                """));
        Assert.Equal("23514", lateShop.SqlState);
    }
}
