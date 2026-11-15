using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0202Tests
{
    [Fact]
    public async Task AccountPages_EnumerateAllOwnAccountsWithBoundedResponses()
    {
        await using var host = await MerchantHostAsync();
        var actor = await host.SignInAsync();
        var foreign = await host.SignInAsync();
        await BillingPaginationFixture.SeedAsync(database, host.Fixture, actor.Actor, 205);
        await BillingPaginationFixture.SeedAsync(database, host.Fixture, foreign.Actor, 7, 1000);
        var expected = (await P0202Fixture.ReadAccountRowsAsync(database, host.Fixture))
            .Where(row => row.MerchantUserId == actor.Actor).Select(row => row.AccountId).Order().ToArray();
        var actual = new List<Guid>();
        string? cursor = null;
        var pages = 0;
        do
        {
            using var response = await actor.Client.GetAsync(new Uri("/api/billing/accounts" +
                (cursor is null ? "" : "?after=" + cursor), UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.InRange((await response.Content.ReadAsByteArrayAsync()).Length, 1, 65536);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            var ids = body.GetProperty("accounts").EnumerateArray().Select(row => row.GetProperty("accountId").GetGuid()).ToArray();
            Assert.InRange(ids.Length, 1, 100);
            actual.AddRange(ids);
            cursor = body.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
            Assert.True(++pages <= 3, "Continuation must advance.");
        } while (cursor is not null);
        Assert.Equal(expected, actual.Order());
        Assert.Equal(3, pages);
        Assert.Equal(actual.Count, actual.Distinct().Count());
        Assert.Equal(7, (await ListThroughMerchantAsync(foreign.Client)).Length);
    }

    [Fact]
    public async Task AccountContinuation_IsSignedAndCannotGrantForeignAuthority()
    {
        using var fixture = await P0201Fixture.CreateAsync(database);
        await using var licensing = P0202Fixture.LicensingApi(fixture);
        using var client = licensing.CreateClient();
        var actor = Guid.NewGuid();
        await BillingPaginationFixture.SeedAsync(database, fixture, actor, 101);
        var cursor = "00000000-0000-0000-0000-000000000064";
        var path = P0202Fixture.AccountsPath + "/after/" + cursor;
        var proof = P0202Fixture.Sign(fixture, actor, P0202Fixture.List, [], data => data["path"] = path);
        using (var tampered = await P0202Fixture.SendAsync(client, proof, path: path.Replace("0064", "0063", StringComparison.Ordinal)))
            await AssertProblemAsync(tampered, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        using (var extra = await P0202Fixture.SendAsync(client, proof, path: path + "?after=" + cursor))
            await AssertProblemAsync(extra, HttpStatusCode.Unauthorized, "Billing.DelegationInvalid");
        Assert.Equal(0L, await P0202Fixture.CountAsync(database, fixture, UseCount));
        using (var page = await P0202Fixture.SendAsync(client, proof, path: path))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            var body = await page.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Single(body.GetProperty("accounts").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("nextCursor").ValueKind);
        }
        var foreign = P0202Fixture.Sign(fixture, Guid.NewGuid(), P0202Fixture.List, [], data => data["path"] = path);
        using var denied = await P0202Fixture.SendAsync(client, foreign, path: path);
        Assert.Equal(HttpStatusCode.OK, denied.StatusCode);
        Assert.Empty((await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accounts").EnumerateArray());
    }
}

internal static class BillingPaginationFixture
{
    internal static async Task SeedAsync(LicensingPostgreSqlFixture database, P0201Fixture fixture,
        Guid actor, int count, int offset = 0)
    {
        await using var connection = new NpgsqlConnection(database.Bootstrap(fixture.Owner.Database).ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            WITH accounts AS (
                INSERT INTO billing_accounts (id, name, created_by_merchant_user_id, creation_key, creation_request_hash, created_at)
                SELECT ('00000000-0000-0000-0000-' || lpad(to_hex(i), 12, '0'))::uuid,
                    repeat('ಕ', 120), $1, gen_random_uuid(), decode(repeat('00', 32), 'hex'), $2
                FROM generate_series(1 + $3, $4 + $3) AS i RETURNING id
            )
            INSERT INTO billing_account_members (id, account_id, merchant_user_id, role, status, revision, created_at)
            SELECT gen_random_uuid(), id, $1, 1, 1, 1, $2 FROM accounts
            """, connection);
        command.Parameters.AddWithValue(actor);
        command.Parameters.AddWithValue(fixture.Clock.Now);
        command.Parameters.AddWithValue(offset);
        command.Parameters.AddWithValue(count);
        await command.ExecuteNonQueryAsync();
    }
}
