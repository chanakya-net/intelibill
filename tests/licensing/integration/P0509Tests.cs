using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// P05-09: end-to-end bounded offline acceptance across real Chromium, protected service worker,
/// IndexedDB, Kestrel merchant and licensing APIs, and separate PostgreSQL databases.
/// </summary>
public sealed partial class P0509Tests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string BrowserPermitKeyId = "p0509-browser-permit-1";
    private const string GoodsName = "P0509 Offline Rice 1kg";
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [Fact]
    public async Task RefreshAndStockNeverExtendOriginalGrant()
    {
        const string device = "p0509-stale-counter";
        var verifiedAt = Utc("2026-11-29T06:35:00Z");
        var reserveAt = Utc("2026-12-01T06:30:00Z");
        var expectedEnd = verifiedAt.AddHours(48);
        var businessClock = new P0201Clock { Now = verifiedAt };
        using var rsa = RSA.Create(2048);
        await using var fixture = await P0213Fixture.StartAsync(
            database,
            businessClock: businessClock,
            merchantServices: ConfigureMerchantJwtClock,
            merchantExtras: new Dictionary<string, string?>
            {
                ["OfflineLicensePermit:LocalKeyId"] = "p0509-stale-key",
                ["OfflineLicensePermit:LocalPrivateKeyPem"] = rsa.ExportPkcs8PrivateKeyPem(),
            });

        var (owner, account, staleOnly) = await ArrangeStaleMonthChangeShopsAsync(fixture, businessClock, verifiedAt, reserveAt);
        using var ownerClient = owner.Client;
        using var staleClient = staleOnly.Client;

        var initialSnapshot = await fixture.LatestSnapshotAsync(owner.ShopId);
        var staleSnapshot = await fixture.LatestSnapshotAsync(staleOnly.ShopId);
        var authority = await ReadAuthorityAsync(ownerClient, owner.ShopId);
        Assert.Equal((verifiedAt, expectedEnd), (
            authority.GetProperty("authorityVerifiedAt").GetDateTimeOffset(),
            authority.GetProperty("validUntil").GetDateTimeOffset()));
        Assert.True(initialSnapshot.Shop!.AccessEndsAt > expectedEnd);
        Assert.True(initialSnapshot.Shop.NextBoundary > expectedEnd);

        var (leaseId, grantId) = await P0507Tests.Run.ReserveAsync(owner, device);
        var first = await P0507Tests.Run.CompleteSnapshotAsync(owner, device, leaseId, grantId);
        Assert.Equal((expectedEnd, "AuthorityFreshness"), (first.ValidUntil, first.Reason));

        await AssertReadsAndReplayKeepDeadlineAsync(fixture, ownerClient, owner.ShopId, initialSnapshot, expectedEnd);
        await AssertLaterStockAndRevisionKeepDeadlineAsync(fixture, owner, ownerClient, device, leaseId, grantId, initialSnapshot, first.Id, expectedEnd);
        var (permitsIssued, refusalCode) = await AssertExactHalfOpenBoundaryAsync(fixture, owner, staleOnly, staleClient, device, leaseId, grantId, staleSnapshot, expectedEnd);
        var deviceRef = await AssertProviderProjectionBoundaryAsync(fixture, account, owner.ShopId, expectedEnd, (int)permitsIssued);

        var evidence = new
        {
            scenario = "refresh-and-stock-48h",
            shopId = owner.ShopId,
            accountId = account,
            deviceRef,
            authorityVerifiedAt = verifiedAt,
            reserveAt,
            expectedEnd,
            lastAllowedInstant = expectedEnd.AddMicroseconds(-1),
            refusedInstant = expectedEnd,
            permitsIssuedOnOriginalGrant = permitsIssued,
            refusalCode,
            staleShopReserveStatus = 503,
            staleShopReserveCode = "Licence.VerificationUnavailable",
        };
        await File.WriteAllTextAsync(Path.Combine(EvidenceDirectory(), "postgres-stale-authority-48h.json"),
            JsonSerializer.Serialize(evidence, IndentedJson));
        output.WriteLine("P05-09 48h stale-authority correlated: {0}", JsonSerializer.Serialize(evidence));
    }

    private static async Task AssertReadsAndReplayKeepDeadlineAsync(
        P0213Fixture fixture, HttpClient ownerClient, Guid shopId, EntitlementSnapshotV1 snapshot, DateTimeOffset expectedEnd)
    {
        for (var i = 0; i < 2; i++)
            Assert.Equal(expectedEnd, (await ReadAuthorityAsync(ownerClient, shopId)).GetProperty("validUntil").GetDateTimeOffset());
        var (replayHttp, replayAck) = await fixture.SendAsync(snapshot);
        using (replayHttp) Assert.True(replayHttp.IsSuccessStatusCode, await replayHttp.Content.ReadAsStringAsync());
        Assert.Equal("Applied", replayAck?.Outcome);
        Assert.Equal(expectedEnd, (await ReadAuthorityAsync(ownerClient, shopId)).GetProperty("validUntil").GetDateTimeOffset());
    }

    private static async Task AssertLaterStockAndRevisionKeepDeadlineAsync(
        P0213Fixture fixture, P0209Fixture.Merchant owner, HttpClient ownerClient, string device,
        Guid leaseId, Guid grantId, EntitlementSnapshotV1 initialSnapshot, Guid firstPermitId, DateTimeOffset expectedEnd)
    {
        fixture.Hosts.Fixture.Clock.Now = fixture.Hosts.Fixture.Clock.Now.AddMinutes(1);
        await SeedGoodsStockAsync(owner, "Batch-2");
        var second = await P0507Tests.Run.CompleteSnapshotAsync(owner, device, leaseId, grantId);
        fixture.Hosts.Fixture.Clock.Now = fixture.Hosts.Fixture.Clock.Now.AddMinutes(1);
        await SeedGoodsStockAsync(owner, "Batch-3");
        var third = await P0507Tests.Run.CompleteSnapshotAsync(owner, device, leaseId, grantId);
        Assert.NotEqual(firstPermitId, second.Id);
        Assert.NotEqual(second.Id, third.Id);
        Assert.Equal((expectedEnd, "AuthorityFreshness"), (second.ValidUntil, second.Reason));
        Assert.Equal((expectedEnd, "AuthorityFreshness"), (third.ValidUntil, third.Reason));

        var freshTime = fixture.Hosts.Fixture.Clock.Now;
        var fresher = initialSnapshot with
        {
            EventId = Guid.NewGuid(),
            Account = initialSnapshot.Account! with
            {
                Revision = initialSnapshot.Account.Revision + 1,
                UpstreamVerifiedAt = freshTime,
            },
            Shop = initialSnapshot.Shop! with
            {
                Revision = initialSnapshot.Shop.Revision + 1,
                UpstreamVerifiedAt = freshTime,
                AccessEndsAt = initialSnapshot.Shop.AccessEndsAt!.Value.AddDays(30),
            },
        };
        var (freshHttp, freshAck) = await fixture.SendAsync(fresher);
        using (freshHttp) Assert.True(freshHttp.IsSuccessStatusCode, await freshHttp.Content.ReadAsStringAsync());
        Assert.Equal("Applied", freshAck?.Outcome);
        Assert.True((await ReadAuthorityAsync(ownerClient, owner.ShopId)).GetProperty("validUntil").GetDateTimeOffset() > expectedEnd);
        var afterRevision = await P0507Tests.Run.CompleteSnapshotAsync(owner, device, leaseId, grantId);
        Assert.Equal((expectedEnd, "AuthorityFreshness"), (afterRevision.ValidUntil, afterRevision.Reason));
    }

    private static async Task<(long PermitsIssued, string RefusalCode)> AssertExactHalfOpenBoundaryAsync(
        P0213Fixture fixture, P0209Fixture.Merchant owner, P0209Fixture.Merchant staleOnly, HttpClient staleClient,
        string device, Guid leaseId, Guid grantId, EntitlementSnapshotV1 staleSnapshot, DateTimeOffset expectedEnd)
    {
        fixture.Hosts.Fixture.Clock.Now = expectedEnd.AddMicroseconds(-1);
        var lastAllowed = await P0507Tests.Run.CompleteSnapshotAsync(owner, device, leaseId, grantId);
        Assert.Equal((expectedEnd, "AuthorityFreshness"), (lastAllowed.ValidUntil, lastAllowed.Reason));

        fixture.Hosts.Fixture.Clock.Now = expectedEnd;
        var permitsBefore = await CountMerchantAsync(fixture, "offline_license_permits", $"shop_id = '{owner.ShopId:D}'");
        var obsBefore = await CountDeviceObservationsAsync(fixture, owner.ShopId);
        var refused = await P0507Tests.Run.StreamAsync(owner, device, leaseId, grantId);
        var refusalCode = refused.GetProperty("refusalCode").GetString()!;
        Assert.Equal(("refused", "OfflinePermit.DeadlineReached"), (refused.GetProperty("status").GetString(), refusalCode));
        Assert.Equal(permitsBefore, await CountMerchantAsync(fixture, "offline_license_permits", $"shop_id = '{owner.ShopId:D}'"));
        Assert.Equal(obsBefore, await CountDeviceObservationsAsync(fixture, owner.ShopId));

        var (staleReplayHttp, staleReplayAck) = await fixture.SendAsync(staleSnapshot);
        using (staleReplayHttp) Assert.True(staleReplayHttp.IsSuccessStatusCode, await staleReplayHttp.Content.ReadAsStringAsync());
        Assert.Equal("Applied", staleReplayAck?.Outcome);
        var staleLeasesBefore = await CountMerchantAsync(fixture, "invoice_leases", $"shop_id = '{staleOnly.ShopId:D}'");
        using (var staleReserve = await staleClient.PostAsJsonAsync("/api/sales/invoice-leases/reserve", new { deviceId = device, blockSize = 25 }))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, staleReserve.StatusCode);
            Assert.Equal("Licence.VerificationUnavailable", await P0206Fixture.CodeAsync(staleReserve));
        }
        Assert.Equal(staleLeasesBefore, await CountMerchantAsync(fixture, "invoice_leases", $"shop_id = '{staleOnly.ShopId:D}'"));
        return (permitsBefore, refusalCode);
    }

    private static async Task<string> AssertProviderProjectionBoundaryAsync(
        P0213Fixture fixture, Guid account, Guid shopId, DateTimeOffset expectedEnd, int expectedOutstandingPermits)
    {
        await DrainBothAsync(fixture);
        using var support = await P0417Tests.SignInAsync(fixture, "support");
        fixture.Hosts.StatusClock.Now = expectedEnd.AddMicroseconds(-1);
        var beforeEnd = await ReadProviderDevicePermitsAsync(support, account, shopId);
        var deviceBefore = Assert.Single(beforeEnd.GetProperty("devices").EnumerateArray());
        Assert.Equal((true, expectedEnd, expectedOutstandingPermits, "Unknown"), (
            deviceBefore.GetProperty("outstanding").GetBoolean(),
            deviceBefore.GetProperty("knownPermitEnd").GetDateTimeOffset(),
            deviceBefore.GetProperty("outstandingPermits").GetInt32(),
            beforeEnd.GetProperty("disconnectedQueues").GetString()));
        Assert.Equal(OfflineDeviceObservationsV1.Limit("AuthorityFreshness"), deviceBefore.GetProperty("limit").GetString());

        fixture.Hosts.StatusClock.Now = expectedEnd;
        var atEnd = await ReadProviderDevicePermitsAsync(support, account, shopId);
        var deviceAt = Assert.Single(atEnd.GetProperty("devices").EnumerateArray());
        Assert.Equal((false, expectedEnd, 0), (
            deviceAt.GetProperty("outstanding").GetBoolean(),
            deviceAt.GetProperty("knownPermitEnd").GetDateTimeOffset(),
            deviceAt.GetProperty("outstandingPermits").GetInt32()));
        return deviceBefore.GetProperty("deviceRef").GetString()!;
    }

    private static async Task<(Merchant Owner, Guid Account, Merchant StaleOnly)> ArrangeStaleMonthChangeShopsAsync(
        P0213Fixture fixture, P0201Clock businessClock, DateTimeOffset verifiedAt, DateTimeOffset reserveAt)
    {
        fixture.Hosts.Fixture.Clock.Now = verifiedAt;
        fixture.Transport.LeaseClock.Now = verifiedAt;

        var (ownerSetup, account) = await OpenAnnualPaidShopAsync(fixture, verifiedAt);
        var (staleSetup, _) = await OpenAnnualPaidShopAsync(fixture, verifiedAt);
        using var ownerSetupClient = ownerSetup.Client;
        using var staleSetupClient = staleSetup.Client;

        SetMerchantAndBusinessClocks(fixture, businessClock, reserveAt);
        return (
            await SignInShopSessionAsync(fixture, ownerSetup),
            account,
            await SignInShopSessionAsync(fixture, staleSetup));
    }

    private static async Task<(Merchant Owner, Guid Account)> OpenAnnualPaidShopAsync(P0213Fixture fixture, DateTimeOffset verifiedAt)
    {
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        var local = verifiedAt.ToOffset(TimeSpan.FromHours(5.5));
        var anchor = new DateTimeOffset(local.Year, local.Month, 1, 0, 0, 0, local.Offset);
        var term = await SeedTermAsync(fixture.Hosts, account, new TermSeed(
            anchor.ToUniversalTime(), anchor.AddYears(1).ToUniversalTime(), Period: "Year", PricePaise: 1499000,
            AnchorLocal: FormattableString.Invariant($"{anchor:yyyy-MM-dd HH:mm:ss}")));
        await P0213Fixture.LinkAsync(account, owner, owner, term);
        await DrainBothAsync(fixture);
        var activated = await fixture.MerchantScalarAsync($"""
            UPDATE public.shop_license_states SET enforcement_activated_at = '{verifiedAt:O}'
            WHERE shop_id = '{owner.ShopId:D}' RETURNING shop_id
            """);
        Assert.Equal(owner.ShopId, activated);
        await SeedGoodsStockAsync(owner, "Batch-1");
        return (owner, account);
    }

    private static void ConfigureMerchantJwtClock(IServiceCollection services) =>
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .PostConfigure<TimeProvider>((bearer, clock) =>
                bearer.TokenValidationParameters.LifetimeValidator = (notBefore, expires, _, _) =>
                {
                    var now = clock.GetUtcNow().UtcDateTime;
                    return (!notBefore.HasValue || notBefore.Value <= now)
                        && (!expires.HasValue || now < expires.Value);
                });

    private static async Task<Merchant> SignInShopSessionAsync(P0213Fixture fixture, Merchant existing)
    {
        var client = fixture.Hosts.Merchant.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await P0201Tests.LoginAsync(client, existing.Email));
        using var switched = await client.PostAsJsonAsync("/api/shops/switch", new { shopId = existing.ShopId });
        var body = await P0418Tests.ReadAsync(switched, HttpStatusCode.OK);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.GetProperty("accessToken").GetString());
        return existing with { Client = client };
    }
}

internal sealed class P0509BrowserFactAttribute : FactAttribute
{
    public P0509BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0509_RUN_BROWSER") != "1")
            Skip = "Set P0509_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
