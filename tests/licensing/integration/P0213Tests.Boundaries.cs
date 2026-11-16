using System.Net;
using System.Text;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Policies;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0213Tests
{
    [Fact]
    public async Task PrivateTransportAndRuntimeRolesFailClosed()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        var request = await P0213Fixture.LinkAsync(account, owner, owner);
        var snapshot = Snapshot(account, owner.ShopId, request, 1, "Unrestricted", 1, P0210Fixture.Start);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        await RefuseUntrustedAsync(fixture, owner, bytes);
        using var first = await fixture.Transport.SignedAsync(bytes, fixture.AuthClock);
        using var simultaneous = P0213Transport.Request(bytes, EntitlementSnapshotsV1.SnapshotPath,
            first.Headers.Authorization!.Parameter);
        var responses = await Task.WhenAll(fixture.MerchantApi.SendAsync(first), fixture.MerchantApi.SendAsync(simultaneous));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized }, responses.Select(r => r.StatusCode).Order().ToArray());
        foreach (var response in responses) response.Dispose();
        Assert.Equal(1L, await EvidenceCountAsync(fixture, owner.ShopId));

        var foreign = snapshot with { EventId = Guid.NewGuid(), AccountId = Guid.NewGuid() };
        Assert.Equal("AffiliationUnresolved", (await SendReceiptAsync(fixture, foreign)).Disposition);
        var missing = snapshot with { EventId = Guid.NewGuid(), Account = null };
        Assert.Equal("MissingScope", (await SendReceiptAsync(fixture, missing)).Disposition);
        await AssertRuntimeIsolationAsync(fixture, owner.ShopId, account);
    }

    private static async Task RefuseUntrustedAsync(P0213Fixture fixture, Merchant owner, byte[] bytes)
    {
        var path = EntitlementSnapshotsV1.SnapshotPath;
        using var customer = P0213Transport.Request(bytes, path, owner.Client.DefaultRequestHeaders.Authorization!.Parameter);
        await ExpectStatusAsync(fixture.MerchantApi, customer, HttpStatusCode.Unauthorized);
        using var wrongPurpose = await fixture.Transport.SignedAsync(bytes, fixture.AuthClock,
            purpose: SignedServiceAssertion.MerchantEvents);
        await ExpectStatusAsync(fixture.MerchantApi, wrongPurpose, HttpStatusCode.Unauthorized);
        using var wrongKey = await fixture.Transport.SignedAsync(bytes, fixture.AuthClock, merchant: true,
            purpose: SignedServiceAssertion.LicensingDelivery);
        await ExpectStatusAsync(fixture.MerchantApi, wrongKey, HttpStatusCode.Unauthorized);

        using var signed = await fixture.Transport.SignedAsync(bytes, fixture.AuthClock);
        using var changedBody = P0213Transport.Request([.. bytes, (byte)' '], path, signed.Headers.Authorization!.Parameter);
        await ExpectStatusAsync(fixture.MerchantApi, changedBody, HttpStatusCode.Unauthorized);
        using var query = P0213Transport.Request(bytes, path + "?alias=1", signed.Headers.Authorization!.Parameter);
        await ExpectStatusAsync(fixture.MerchantApi, query, HttpStatusCode.Unauthorized);

        var expiredClock = new P0201Clock { Now = fixture.AuthClock.GetUtcNow().AddSeconds(-60) };
        using var expired = await fixture.Transport.SignedAsync(bytes, expiredClock);
        await ExpectStatusAsync(fixture.MerchantApi, expired, HttpStatusCode.Unauthorized);
        var duplicate = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)[..^1] + ",\"schemaVersion\":1}");
        using var malformed = await fixture.Transport.SignedAsync(duplicate, fixture.AuthClock);
        await ExpectStatusAsync(fixture.MerchantApi, malformed, HttpStatusCode.BadRequest);

        using var wrongDirection = await fixture.Transport.SignedAsync(bytes, fixture.AuthClock,
            path: EntitlementSnapshotsV1.AcknowledgementPath);
        await ExpectStatusAsync(fixture.LicensingApi, wrongDirection, HttpStatusCode.Unauthorized);
    }

    private static async Task ExpectStatusAsync(HttpClient client, HttpRequestMessage request, HttpStatusCode expected)
    {
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    private static async Task AssertRuntimeIsolationAsync(P0213Fixture fixture, Guid shop, Guid account)
    {
        var runtime = fixture.Hosts.Fixture.MerchantRuntime;
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(runtime,
            "SELECT count(*) FROM public.license_grant_evidence"));
        Assert.Equal(0L, await LicensingPostgreSqlFixture.ReadScalarAsync(runtime,
            "SELECT count(*) FROM public.account_license_restrictions"));
        var denied = await Assert.ThrowsAsync<PostgresException>(() =>
            LicensingPostgreSqlFixture.ReadScalarAsync(new NpgsqlConnectionStringBuilder(runtime.ConnectionString)
                { Database = fixture.Hosts.Fixture.Runtime.Database }, "SELECT 1"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);

        await using var pooled = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(runtime.ConnectionString)
            { Pooling = true, MaxPoolSize = 1 }.ConnectionString);
        await using (var scoped = await pooled.OpenConnectionAsync())
        {
            await using var tx = await scoped.BeginTransactionAsync();
            await using var command = new NpgsqlCommand($"""
                SELECT set_config('app.active_shop_id', '{shop:D}', true),
                    set_config('app.entitlement_account_id', '{account:D}', true);
                """, scoped, tx);
            await command.ExecuteNonQueryAsync();
            command.CommandText = "SELECT count(*) FROM public.claim_entitlement_delivery(now(), gen_random_uuid())";
            Assert.Equal(0L, await command.ExecuteScalarAsync());
            command.CommandText = "UPDATE public.license_grant_evidence SET digest = digest";
            var evidence = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("55000", evidence.SqlState);
            await tx.RollbackAsync();
        }
        await using var reused = await pooled.OpenConnectionAsync();
        await using var query = new NpgsqlCommand("SELECT count(*) FROM public.license_grant_evidence", reused);
        Assert.Equal(0L, await query.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ProjectionNeverEnablesEnforcementAndCommerceSurvivesOutage()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        var request = await P0213Fixture.LinkAsync(account, owner, owner);
        var now = P0210Fixture.Start;
        var restricted = Snapshot(account, owner.ShopId, request, 5, "Trading", 5, now);
        await ExpectAppliedAsync(fixture, restricted);
        var (shop, head) = await HeadsAsync(owner.ShopId, account);
        Assert.Null(shop.EnforcementActivatedAt);
        Assert.Equal(LicenseProjectionDecision.Restricted, Evaluate(shop, head, now));
        await P0209Tests.PostStockAsync(owner.Client, "Stock during restricted projection");

        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.MerchantDb, $"""
            UPDATE public.shop_license_states SET enforcement_activated_at = '2027-02-09T00:00:00Z'
            WHERE shop_id = '{owner.ShopId:D}'
            """);
        var newer = restricted with { EventId = Guid.NewGuid(), Shop = restricted.Shop! with { Revision = 6 } };
        await ExpectAppliedAsync(fixture, newer);
        Assert.Equal(Utc("2027-02-09T00:00:00Z"), (await HeadsAsync(owner.ShopId, account)).Item1.EnforcementActivatedAt);
        await P0209Tests.PostStockAsync(owner.Client, "Stock after newer entitlement");
    }
}
