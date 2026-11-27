using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Licensing.Domain.Entities;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0209Tests
{
    [Fact]
    public async Task StatusWaitsForBillingManagerRemovalBeforeDisclosingCommercialFacts()
    {
        await using var host = await StartAsync(database, Utc("2027-02-10T00:00:00Z"), licensingPoolSize: 2);
        var payer = await SignInAsync(host);
        var account = await CreateAccountAsync(payer.Client);
        var owner = await SignInAsync(host, ShopRole.Owner);
        var manager = await SignInAsync(host, ShopRole.Staff, owner.ShopId);
        var membership = await P0204Tests.InviteManagerAsync(payer.Client, account, manager.Client, manager.Email);
        var term = await SeedTermAsync(host, account, new TermSeed(Jan31, Feb28, AnchorLocal: "2027-01-31 10:00:00"));
        await AffiliateAsync(host, account, payer.UserId, owner.ShopId, owner.UserId, term, Jan31, Feb28);
        var accountPath = $"/api/billing/accounts/{account:D}/subscriptions";

        using var baselineShop = await ShopStatusAsync(manager.Client, owner.ShopId);
        var baseline = await ReadAsync(baselineShop);
        AssertCommercialShop(baseline, account);
        using var baselineAccount = await manager.Client.GetAsync(accountPath);
        AssertCommercialAccount(await ReadAsync(baselineAccount));

        await using var gate = new NpgsqlConnection(host.LicensingOwner.ConnectionString);
        await gate.OpenAsync();
        await using var monitor = new NpgsqlConnection(host.LicensingOwner.ConnectionString);
        await monitor.OpenAsync();
        var control = await ReadWhileBillingManagerLockedAsync(gate, monitor, membership, manager, accountPath, remove: false);
        AssertCommercialShop(control.Shop, account);
        Assert.Equal(HttpStatusCode.OK, control.AccountStatus);
        using var controlAccount = JsonDocument.Parse(control.AccountBody);
        AssertCommercialAccount(controlAccount.RootElement);

        var removed = await ReadWhileBillingManagerLockedAsync(gate, monitor, membership, manager, accountPath, remove: true);
        AssertRedactedShop(removed.Shop);
        Assert.Equal(Without(baseline, "accountId", "commercial"), Without(removed.Shop, "accountId", "commercial"));
        Assert.Equal(HttpStatusCode.Forbidden, removed.AccountStatus);
        Assert.DoesNotContain("149900", removed.AccountBody);
    }

    private static async Task<(JsonElement Shop, HttpStatusCode AccountStatus, string AccountBody)> ReadWhileBillingManagerLockedAsync(
        NpgsqlConnection gate, NpgsqlConnection monitor, Guid membership, Merchant manager, string accountPath, bool remove)
    {
        await using var barrier = await gate.BeginTransactionAsync();
        await HoldRowAsync(barrier, "SELECT id FROM billing_account_members WHERE id = $1 FOR NO KEY UPDATE", membership);
        var shopRead = ShopStatusAsync(manager.Client, manager.ShopId);
        var accountRead = manager.Client.GetAsync(accountPath);
        await WaitForStatusLocksAsync(monitor, "%billing_account_members%FOR SHARE%", shopRead, accountRead);
        if (remove)
        {
            await using var update = new NpgsqlCommand("""
                UPDATE billing_account_members SET status = $2, revision = revision + 1 WHERE id = $1
                """, gate, barrier);
            update.Parameters.AddWithValue(membership);
            update.Parameters.AddWithValue((int)BillingAccountMemberStatus.Removed);
            Assert.Equal(1, await update.ExecuteNonQueryAsync());
            await barrier.CommitAsync();
        }
        else
        {
            await barrier.RollbackAsync();
        }

        using var shop = await shopRead;
        using var account = await accountRead;
        return (await ReadAsync(shop), account.StatusCode, await account.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(AdminCapability.Support)]
    [InlineData(AdminCapability.SecurityAdministrator)]
    public async Task ProviderStatusWaitsForCapabilityChangeBeforeDisclosingCommercialFacts(AdminCapability newGrant)
    {
        await using var fixture = await P0206Fixture.StartAsync(database);
        var (account, shop, _) = await SeedProviderCoverageAsync(fixture);
        string seed;
        using (var owner = await fixture.SignInOwnerAsync())
            seed = await fixture.EnrollOperatorAsync(owner);
        await fixture.GrantAsync(P0114Tests.Invitee, "billing");
        using var billing = await fixture.SignInOperatorAsync(seed);
        var path = $"/api/admin/accounts/{account:D}/shops/{shop:D}/status";
        using var baseline = await billing.SendAsync(HttpMethod.Get, path);
        AssertCommercialShop(await ReadAsync(baseline), account);

        await using var gate = new NpgsqlConnection(fixture.Superuser.ConnectionString);
        await gate.OpenAsync();
        await using var monitor = new NpgsqlConnection(fixture.Superuser.ConnectionString);
        await monitor.OpenAsync();
        var identity = await BillingOperatorIdAsync(gate);
        await using var barrier = await gate.BeginTransactionAsync();
        await HoldRowAsync(barrier, "SELECT id FROM admin_identities WHERE id = $1 FOR UPDATE", identity);
        var read = billing.SendAsync(HttpMethod.Get, path);
        await WaitForStatusLocksAsync(monitor, "%admin_identities%FOR SHARE%", read);
        await using var update = new NpgsqlCommand("UPDATE admin_identities SET capabilities = $2 WHERE id = $1", gate, barrier);
        update.Parameters.AddWithValue(identity);
        update.Parameters.AddWithValue((int)newGrant);
        Assert.Equal(1, await update.ExecuteNonQueryAsync());
        await barrier.CommitAsync();

        using var response = await read;
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("149900", body);
        if (newGrant == AdminCapability.Support)
            AssertRedactedShop(await ReadAsync(response));
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("Status.AccessDenied", await P0206Fixture.CodeAsync(response));
        }
    }

    private static async Task<Guid> BillingOperatorIdAsync(NpgsqlConnection connection)
    {
        await using var sql = new NpgsqlCommand("SELECT id FROM admin_identities WHERE capabilities = $1", connection);
        sql.Parameters.AddWithValue((int)AdminCapability.Billing);
        return Assert.IsType<Guid>(await sql.ExecuteScalarAsync());
    }

    private static async Task HoldRowAsync(NpgsqlTransaction barrier, string query, Guid id)
    {
        await using var sql = new NpgsqlCommand(query, barrier.Connection, barrier);
        sql.Parameters.AddWithValue(id);
        Assert.Equal(id, await sql.ExecuteScalarAsync());
    }

    private static async Task WaitForStatusLocksAsync(NpgsqlConnection monitor, string queryPattern,
        params Task<HttpResponseMessage>[] reads)
    {
        await using var sql = new NpgsqlCommand("""
            SELECT count(*) FROM pg_stat_activity
            WHERE datname = current_database() AND usename = 'licensing_runtime' AND wait_event_type = 'Lock'
                AND query LIKE $1
            """, monitor);
        sql.Parameters.AddWithValue(queryPattern);
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            if ((long)(await sql.ExecuteScalarAsync())! == reads.Length) return;
            Assert.False(reads.Any(read => read.IsCompleted), $"Status returned before waiting on {queryPattern}.");
            await Task.Delay(10);
        }
        Assert.Fail($"Expected {reads.Length} PostgreSQL backends waiting on {queryPattern}.");
    }

    private static void AssertCommercialShop(JsonElement status, Guid account)
    {
        Assert.Equal(account, status.GetProperty("accountId").GetGuid());
        AssertPrice(status.GetProperty("term"));
    }

    private static void AssertCommercialAccount(JsonElement status) =>
        AssertPrice(Assert.Single(status.GetProperty("items").EnumerateArray()).GetProperty("status"));

    private static void AssertPrice(JsonElement term) =>
        Assert.Equal(149900, term.GetProperty("commercial").GetProperty("pricePaise").GetInt64());

    private static void AssertRedactedShop(JsonElement status)
    {
        Assert.Equal(JsonValueKind.Null, status.GetProperty("accountId").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("term").GetProperty("commercial").ValueKind);
        Assert.DoesNotContain("149900", status.GetRawText());
    }
}
