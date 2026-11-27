using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0215Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0215Tests
{
    [Fact]
    public async Task ConcurrentSchedulingAndCoverageChangesCannotChangeAcceptedScope()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var first = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var second = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var third = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(first.Client);
        var (term, _) = await SeedCurrentTermAsync(fixture, account, capacity: 3);
        await P0213Fixture.LinkAsync(account, first, first, term);
        var path = CancellationPath(account, term);
        var oldPreview = await PreviewAsync(first, path);
        await ApplyRequestAsync(second, await RequestAsync(first, second, account, term));
        using var changed = await first.Client.PostAsJsonAsync(path, Accept(oldPreview, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);

        var consent = await RequestAsync(first, third, account, term);
        var preview = await PreviewAsync(first, path);
        Assert.Equal(2, preview.GetProperty("affectedShopCount").GetInt32());
        var command = Accept(preview, Guid.NewGuid());
        var different = Accept(preview, Guid.NewGuid());
        await AssertSingleEffectAsync(first, path, command, different);
        Assert.Equal(1L, await fixture.Trial.CountAsync("subscription_cancellation_receipts"));
        Assert.Equal(new[] { first.ShopId, second.ShopId }.Order(), (await PreviewAsync(first, path))
            .GetProperty("affectedShops").EnumerateArray().Select(shop => shop.GetProperty("shopId").GetGuid()).Order());
        await AssertAttachmentDeniedAsync(fixture, first, third, account, term, consent);
    }

    private static async Task AssertSingleEffectAsync(Merchant payer, string path, object same, object different)
    {
        var responses = await Task.WhenAll(payer.Client.PostAsJsonAsync(path, same), payer.Client.PostAsJsonAsync(path, same),
            payer.Client.PostAsJsonAsync(path, different));
        try
        {
            Assert.Contains(responses[2].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict });
            var sameWon = responses[2].StatusCode == HttpStatusCode.Conflict;
            Assert.Equal(sameWon ? HttpStatusCode.OK : HttpStatusCode.Conflict, responses[0].StatusCode);
            Assert.Equal(responses[0].StatusCode, responses[1].StatusCode);
            if (sameWon) Assert.True(JsonElement.DeepEquals(await ReadAsync(responses[0]), await ReadAsync(responses[1])));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    private static async Task AssertAttachmentDeniedAsync(P0213Fixture fixture, Merchant first, Merchant third, Guid account,
        Guid term, string consent)
    {
        Assert.Equal("Granted", await P0205Tests.DecideAsync(third.Client, consent, "grant"));
        Assert.Equal("Accepted", await P0205Tests.DecideAsync(third.Client, consent, "accept"));
        await P0205Tests.WaitForConsentAsync(third.Client, consent, "NeedsReview");
        Assert.Equal(0L, await fixture.SourceScalarAsync($"SELECT count(*) FROM shop_affiliations WHERE shop_id = '{third.ShopId:D}'"));
        using var response = await first.Client.GetAsync(new Uri($"/api/billing/accounts/{account:D}/affiliations", UriKind.Relative));
        var terms = (await ReadAsync(response)).GetProperty("terms");
        Assert.DoesNotContain(terms.EnumerateArray(), row => row.GetProperty("termId").GetGuid() == term);
    }

    [Fact]
    public async Task CompleteSourceAndFrozenScopePageBeyondOneHundredShops()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var payer = await SignInAsync(fixture.Hosts);
        var account = await CreateAccountAsync(payer.Client);
        var term = await SeedTermAsync(fixture.Hosts, account,
            new(StartsAt, EndsAt, AnchorLocal: "2027-01-31 10:00:00", Grouped: true, Capacity: 101));
        var shops = Enumerable.Range(1, 101).Select(_ => Guid.NewGuid()).Order().ToArray();
        foreach (var shop in shops)
            await AffiliateAsync(fixture.Hosts, account, payer.UserId, shop, Guid.NewGuid(), term, StartsAt, EndsAt);
        var path = CancellationPath(account, term);
        var first = await PreviewAsync(payer, path);
        Assert.Equal(101, first.GetProperty("affectedShopCount").GetInt32());
        Assert.Equal(100, first.GetProperty("affectedShops").GetArrayLength());
        await AssertLastPageAsync(payer, path, first, shops[^1]);
        using var scheduled = await payer.Client.PostAsJsonAsync(path, Accept(first, Guid.NewGuid()));
        await ReadAsync(scheduled);
        var saved = await PreviewAsync(payer, path);
        Assert.Equal(first.GetProperty("affectedShopSetHash").GetString(), saved.GetProperty("affectedShopSetHash").GetString());
        await AssertLastPageAsync(payer, path, saved, shops[^1]);
    }

    private static async Task AssertLastPageAsync(Merchant payer, string path, JsonElement first, Guid shop)
    {
        var next = await PreviewAsync(payer, path + $"?after={first.GetProperty("nextCursor").GetGuid():D}");
        Assert.Equal(first.GetProperty("sourceHash").GetString(), next.GetProperty("sourceHash").GetString());
        Assert.Equal(first.GetProperty("affectedShopSetHash").GetString(), next.GetProperty("affectedShopSetHash").GetString());
        Assert.Equal(shop, Assert.Single(next.GetProperty("affectedShops").EnumerateArray()).GetProperty("shopId").GetGuid());
    }

    [Fact]
    public async Task ScheduledCancellationKeepsOriginalFactsAndIsolatesRetainedEvidence()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        var term = await SeedPaidAsync(fixture.Hosts, owner, account, owner);
        var before = await PaidFactsAsync(fixture, term);
        var path = CancellationPath(account, term);
        using var scheduled = await owner.Client.PostAsJsonAsync(path, Accept(await PreviewAsync(owner, path), Guid.NewGuid()));
        await ReadAsync(scheduled);
        await CheckIsolationAsync(fixture, account, owner);
        await AssertIncompleteEvidenceRejectedAsync(fixture, owner, account);
        Assert.Equal(before, await PaidFactsAsync(fixture, term));
    }

    private static async Task CheckIsolationAsync(P0213Fixture fixture, Guid account, Merchant owner)
    {
        var settings = fixture.Hosts.Fixture.Runtime;
        Assert.Equal("0,0,0", await ScopedAsync(settings, null, null, null));
        Assert.Equal("0,0,0", await ScopedAsync(settings, Guid.NewGuid(), null, owner.UserId));
        Assert.Equal("1,1,0", await ScopedAsync(settings, null, owner.ShopId, null));
        Assert.Equal("1,1,0", await ScopedAsync(settings, account, null, null));
        Assert.Equal("1,1,1", await ScopedAsync(settings, account, null, owner.UserId));
        foreach (var table in new[] { "scheduled_subscription_changes", "scheduled_subscription_change_shops", "subscription_cancellation_receipts" })
        {
            foreach (var command in new[] { $"UPDATE {table} SET change_id = change_id", $"DELETE FROM {table}", $"TRUNCATE {table} CASCADE" })
            {
                var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                    LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, command));
                Assert.Equal("55000", exception.SqlState);
            }
        }
    }

    private static async Task AssertIncompleteEvidenceRejectedAsync(P0213Fixture fixture, Merchant payer, Guid account)
    {
        var other = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var unscheduled = await SeedPaidAsync(fixture.Hosts, payer, account, other);
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
                INSERT INTO scheduled_subscription_changes
                    (change_id, account_id, term_id, revision, kind, effective_at, recorded_at, policy_version, shop_count, shop_hash)
                SELECT gen_random_uuid(), account_id, term_id, 1, 'ScheduleCancellation', ends_at,
                    starts_at, 'term-boundary/1', 1, repeat('0', 64)
                FROM subscription_coverage_terms WHERE term_id = '{unscheduled:D}'
                """));
        Assert.Equal("23514", error.SqlState);
    }

    private static async Task<string?> ScopedAsync(NpgsqlConnectionStringBuilder settings, Guid? account, Guid? shop, Guid? actor)
    {
        await using var connection = new NpgsqlConnection(settings.ConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using var scope = new NpgsqlCommand("""
            SELECT set_config('app.billing_account_id', $1, true), set_config('app.status_shop_id', $2, true),
                set_config('app.billing_actor_id', $3, true)
            """, connection, tx);
        foreach (var id in new[] { account, shop, actor }) scope.Parameters.AddWithValue(id?.ToString("D") ?? "");
        await scope.ExecuteNonQueryAsync();
        await using var counts = new NpgsqlCommand("""
            SELECT concat_ws(',', (SELECT count(*) FROM scheduled_subscription_changes),
                (SELECT count(*) FROM scheduled_subscription_change_shops), (SELECT count(*) FROM subscription_cancellation_receipts))
            """, connection, tx);
        return (string?)await counts.ExecuteScalarAsync();
    }

    private static Task<object?> PaidFactsAsync(P0213Fixture fixture, Guid term) => fixture.SourceScalarAsync($"""
        SELECT jsonb_build_array(to_jsonb(t), to_jsonb(f))::text
        FROM subscription_coverage_terms t JOIN subscription_term_facts f USING (account_id, term_id)
        WHERE term_id = '{term:D}'
        """);
}
