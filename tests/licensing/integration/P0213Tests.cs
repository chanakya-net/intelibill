using System.Net;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Policies;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0213Tests(LicensingPostgreSqlFixture database)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [Fact]
    public async Task TrialCommitDeliversOneAuthenticatedProjection()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        var request = await P0213Fixture.LinkAsync(account, owner, owner);
        Assert.Equal(1L, await fixture.Trial.CountAsync("entitlement_snapshots"));
        var operation = fixture.Trial.Accept();
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(owner, operation)).Status);
        Assert.Equal(1L, await fixture.Trial.CountAsync("trial_history"));
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(owner, operation)).Status);
        Assert.Equal(2L, await fixture.Trial.CountAsync("entitlement_snapshots"));

        var original = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.Equal(request, original.AffiliationRequestId);
        Assert.Equal(0L, await EvidenceCountAsync(fixture, owner.ShopId));
        await fixture.DrainAsync(source: true);
        Assert.Equal(2L, await EvidenceCountAsync(fixture, owner.ShopId));
        Assert.Equal("AwaitingAcknowledgement", await OutboxAsync(fixture, original.EventId));
        await fixture.DrainAsync(source: false);
        Assert.Equal("Applied", await OutboxAsync(fixture, original.EventId));

        var (shop, restriction) = await HeadsAsync(owner.ShopId, account);
        Assert.Null(shop.EnforcementActivatedAt);
        Assert.Equal(original.Shop!.Revision, shop.Revision);
        Assert.Equal(original.Shop.UpstreamVerifiedAt, shop.UpstreamVerifiedAt);
        Assert.Equal(P0210Fixture.Start.AddDays(14), shop.AccessEndsAt);
        Assert.Equal(LicenseProjectionDecision.VerifiedPermission, Evaluate(shop, restriction, P0210Fixture.Start));
        await AssertRetainedMigrationsAsync(fixture);
    }

    [Fact]
    public async Task ReorderedScopesCannotEraseRestrictionOrReviveAccess()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var first = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var second = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var unrelated = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(first.Client);
        var otherAccount = await CreateAccountAsync(unrelated.Client);
        var firstRequest = await P0213Fixture.LinkAsync(account, first, first);
        var secondRequest = await P0213Fixture.LinkAsync(account, first, second);
        var otherRequest = await P0213Fixture.LinkAsync(otherAccount, unrelated, unrelated);
        var now = P0210Fixture.Start;
        var initial = Snapshot(account, first.ShopId, firstRequest, 8, "Trading", 5, now);
        await ExpectAppliedAsync(fixture, Snapshot(otherAccount, unrelated.ShopId, otherRequest, 1, "Unrestricted", 1, now));
        await ExpectAppliedAsync(fixture, Snapshot(account, second.ShopId, secondRequest, 7, "Unrestricted", 1, now));
        await ExpectAppliedAsync(fixture, initial);
        await ExpectAppliedAsync(fixture, Snapshot(account, first.ShopId, firstRequest, 7, "Unrestricted", 6, now));

        var restricted = await HeadsAsync(second.ShopId, account);
        Assert.Equal(LicenseProjectionDecision.Restricted, Evaluate(restricted.Item1, restricted.Item2, now));
        var other = await HeadsAsync(unrelated.ShopId, otherAccount);
        Assert.Equal(LicenseProjectionDecision.VerifiedPermission, Evaluate(other.Item1, other.Item2, now));
        await AssertSupersessionAsync(fixture, initial);
    }

    private async Task AssertSupersessionAsync(P0213Fixture fixture, EntitlementSnapshotV1 initial)
    {
        var now = P0210Fixture.Start;
        var higherAccount = initial with
        {
            EventId = Guid.NewGuid(), Account = initial.Account! with { Revision = 9, Restriction = "Security" },
        };
        await ExpectAppliedAsync(fixture, higherAccount);
        var accidentalRelease = higherAccount with
        {
            EventId = Guid.NewGuid(), Account = higherAccount.Account! with { Revision = 10, Restriction = "Unrestricted" },
        };
        Assert.Equal("ConflictingAccountScope", (await SendReceiptAsync(fixture, accidentalRelease)).Disposition);
        var release = accidentalRelease with
        {
            EventId = Guid.NewGuid(), Account = accidentalRelease.Account! with { ReleasedRevision = 9 },
        };
        await ExpectAppliedAsync(fixture, release);
        var replays = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => SendReceiptAsync(fixture, release)));
        Assert.All(replays, ack => Assert.Equal(replays[0], ack));
        var prior = higherAccount with { EventId = Guid.NewGuid() };
        Assert.Equal("Superseded", (await SendReceiptAsync(fixture, prior)).Disposition);
        var equivocation = release with { Shop = release.Shop! with { MemberAllowance = 6 } };
        using var conflict = (await fixture.SendAsync(equivocation)).Http;
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var (shop, account) = await HeadsAsync(initial.ShopId, initial.AccountId);
        Assert.Equal(6, shop.Revision);
        Assert.Equal(10, account.Revision);
        Assert.Equal("Unrestricted", account.Restriction);
        Assert.Equal(LicenseProjectionDecision.VerifiedPermission, Evaluate(shop, account, now.AddDays(1).AddTicks(-10)));
        Assert.Equal(LicenseProjectionDecision.Expired, Evaluate(shop, account, now.AddDays(1)));
        Assert.Equal(LicenseProjectionDecision.Expired, Evaluate(shop, account, now.AddDays(1).AddTicks(10)));
    }

    internal static EntitlementSnapshotV1 Snapshot(Guid account, Guid shop, Guid request, long accountRevision,
        string restriction, long shopRevision, DateTimeOffset now) =>
        new(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(), account, shop, request,
            new(accountRevision, now, restriction, null),
            new(shopRevision, now, "Active", Guid.NewGuid(), "Trial", now, now.AddDays(1), null, 1, 5));

    internal static async Task<EntitlementAcknowledgementV1> SendReceiptAsync(P0213Fixture fixture, EntitlementSnapshotV1 snapshot)
    {
        var (response, ack) = await fixture.SendAsync(snapshot);
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            Assert.NotNull(ack);
            return ack;
        }
    }

    internal static async Task ExpectAppliedAsync(P0213Fixture fixture, EntitlementSnapshotV1 snapshot) =>
        Assert.Equal("Applied", (await SendReceiptAsync(fixture, snapshot)).Disposition);

    internal static async Task<long> EvidenceCountAsync(P0213Fixture fixture, Guid shop) =>
        (long)(await fixture.MerchantScalarAsync($"SELECT count(*) FROM public.license_grant_evidence WHERE shop_id = '{shop:D}'"))!;

    internal static Task<object?> OutboxAsync(P0213Fixture fixture, Guid id) =>
        fixture.SourceScalarAsync($"SELECT status FROM public.entitlement_outbox WHERE event_id = '{id:D}'");

    private async Task<(ShopLicenseState, AccountLicenseRestriction)> HeadsAsync(Guid shop, Guid account)
    {
        await using var context = MerchantDb(database);
        return (await context.Set<ShopLicenseState>().SingleAsync(row => row.ShopId == shop),
            await context.Set<AccountLicenseRestriction>().SingleAsync(row => row.AccountId == account));
    }

    private static LicenseProjectionDecision Evaluate(ShopLicenseState shop, AccountLicenseRestriction account, DateTimeOffset now) =>
        LicenseProjectionPolicy.Evaluate(shop, account, now, EntitlementSnapshotsV1.Freshness);
}
