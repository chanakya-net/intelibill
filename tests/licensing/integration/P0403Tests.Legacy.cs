using System.Net;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0403Tests
{
    [Fact]
    public async Task UpgradePreservesLegacyAccountRestrictionConsistentlyAcrossReadsAndFence()
    {
        await using var isolated = new IsolatedLicensingPostgreSqlFixture();
        await isolated.InitializeAsync();
        await VerifyLegacyUpgradeAsync(isolated);
    }

    private static async Task VerifyLegacyUpgradeAsync(LicensingPostgreSqlFixture isolated)
    {
        await using var fixture = await P0213Fixture.StartAsync(isolated,
            businessClock: new P0201Clock { Now = P0202Fixture.WholeSecondNow() });
        var legacy = await CreateTradingShopAsync(fixture.Hosts);
        var sibling = await CreateTradingShopAsync(fixture.Hosts);
        await LinkAsync(fixture, legacy);
        var siblingAccount = await LinkAsync(fixture, sibling);
        var original = await fixture.LatestSnapshotAsync(legacy.ShopId);
        var snapshot = original with { Account = original.Account! with { Restriction = "Security" } };
        await using var db = MerchantDb(isolated);
        await db.GetService<IMigrator>().MigrateAsync("20261118090000_BoundMerchantEntitlementReconciliation");
        await WritePredecessorProjectionAsync(db, fixture, snapshot);
        await EnrollAsync(fixture, legacy);
        await isolated.PrepareMerchantAsync();

        await AssertLegacySecurityAsync(legacy);
        var (http, confirmation) = await fixture.SendAsync(await fixture.LatestSnapshotAsync(sibling.ShopId));
        http.Dispose();
        Assert.Equal("Applied", confirmation?.Outcome);
        await EnrollAsync(fixture, sibling);
        using var authority = await ReadAuthorityAsync(sibling);
        Assert.Equal(HttpStatusCode.OK, authority.StatusCode);

        using var admin = await SignInSecurityAsync(fixture);
        var effective = fixture.Hosts.Fixture.Clock.Now.AddSeconds(10);
        await ApplySuspensionAsync(admin, siblingAccount, "Account", null, "Security", effective);
        var (future, applied) = await fixture.SendAsync(await fixture.LatestSnapshotAsync(sibling.ShopId));
        future.Dispose();
        Assert.Equal("Applied", applied?.Outcome);
        await AssertReadableAsync(sibling);
        using var before = await ReadAuthorityAsync(sibling);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        fixture.Hosts.Fixture.Clock.Now = effective;
        await AssertLegacySecurityAsync(sibling);
        await AssertLegacySecurityAsync(legacy);
    }

    private static async Task WritePredecessorProjectionAsync(ApplicationDbContext db, P0213Fixture fixture,
        EntitlementSnapshotV1 snapshot)
    {
        using var message = await fixture.Transport.SignedAsync(snapshot, fixture.AuthClock);
        var payload = await message.Content!.ReadAsByteArrayAsync();
        var now = EntitlementSnapshotsV1.Instant(fixture.AuthClock);
        var account = snapshot.Account!;
        var shop = snapshot.Shop!;
        var settings = new Dictionary<string, string?>();
        fixture.Transport.AddSettings(settings, licensing: false);
        db.Add(new LicenseGrantEvidence
        {
            EventId = snapshot.EventId, AccountId = snapshot.AccountId, ShopId = snapshot.ShopId,
            AffiliationRequestId = snapshot.AffiliationRequestId, Payload = payload,
            AccountRevision = account.Revision, ShopRevision = shop.Revision,
            AccountUpstreamVerifiedAt = account.UpstreamVerifiedAt, ShopUpstreamVerifiedAt = shop.UpstreamVerifiedAt,
            IssuedAt = now, ReceivedAt = now, AppliedAt = now, KeyId = "fixture-snapshot",
            PublicKey = settings["EntitlementDelivery:PublicKeys:fixture-snapshot"]!,
            Assertion = message.Headers.Authorization!.Parameter!, Digest = SignedServiceAssertion.Digest(payload),
        });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO account_license_restrictions (account_id, revision, upstream_verified_at, restriction, evidence_id)
            VALUES ({snapshot.AccountId}, {account.Revision}, {account.UpstreamVerifiedAt}, {account.Restriction}, {snapshot.EventId});
            INSERT INTO shop_license_states (shop_id, account_id, affiliation_request_id, revision, upstream_verified_at,
                state, term_id, basis, starts_at, access_ends_at, next_boundary, shop_capacity, member_allowance,
                evidence_id)
            VALUES ({snapshot.ShopId}, {snapshot.AccountId}, {snapshot.AffiliationRequestId}, {shop.Revision},
                {shop.UpstreamVerifiedAt}, {shop.State}, {shop.TermId}, {shop.Basis}, {shop.StartsAt}, {shop.AccessEndsAt},
                {shop.NextBoundary}, {shop.ShopCapacity}, {shop.MemberAllowance}, {snapshot.EventId});
            """);
    }

    private static async Task AssertLegacySecurityAsync(Merchant shop)
    {
        using var authority = await ReadAuthorityAsync(shop);
        Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(authority));
        foreach (var path in new[] { "/api/sales", "/api/exports/sales" })
        {
            using var read = await shop.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
            Assert.Equal("Licence.SecuritySuspended", await P0206Fixture.CodeAsync(read));
        }
    }
}
