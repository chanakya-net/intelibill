using System.Net;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Policies;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0213Tests
{
    [Fact]
    public async Task CrashRetryAndLeaseTakeoverPreserveEvidenceAndFreshness()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var accountId = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(accountId, owner, owner);
        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(owner, fixture.Trial.Accept())).Status);
        var snapshot = await fixture.LatestSnapshotAsync(owner.ShopId);

        await DiscardSnapshotResponseAsync(fixture, snapshot, owner.ShopId);
        var originalTimes = await EvidenceTimesAsync(fixture, snapshot.EventId);
        await StealExpiredLeaseAsync(fixture, snapshot);
        await DiscardAcknowledgementResponseAsync(fixture, snapshot);
        Assert.Equal(originalTimes, await EvidenceTimesAsync(fixture, snapshot.EventId));
        Assert.Equal(2L, await EvidenceCountAsync(fixture, owner.ShopId));

        var (shop, restriction) = await HeadsAsync(owner.ShopId, accountId);
        var boundary = P0210Fixture.Start.AddHours(48);
        Assert.Equal(LicenseProjectionDecision.VerifiedPermission, Evaluate(shop, restriction, boundary.AddTicks(-10)));
        Assert.Equal(LicenseProjectionDecision.Stale, Evaluate(shop, restriction, boundary));
        Assert.Equal(LicenseProjectionDecision.Stale, Evaluate(shop, restriction, boundary.AddTicks(10)));
    }

    internal static async Task DiscardSnapshotResponseAsync(P0213Fixture fixture, EntitlementSnapshotV1 snapshot, Guid shop)
    {
        var before = await EvidenceCountAsync(fixture, shop);
        fixture.Transport.AfterCommit = (request, response, _) =>
        {
            Assert.Equal(EntitlementSnapshotsV1.SnapshotPath, request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            throw new HttpRequestException("Local fixture discarded committed snapshot response.");
        };
        Assert.True(await fixture.DeliverAsync(source: true));
        fixture.Transport.AfterCommit = null;
        Assert.Equal(before + 1, await EvidenceCountAsync(fixture, shop));
        Assert.Equal("Pending", await OutboxAsync(fixture, snapshot.EventId));
    }

    private static async Task StealExpiredLeaseAsync(P0213Fixture fixture, EntitlementSnapshotV1 snapshot)
    {
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.LeaseClock.Now += TimeSpan.FromSeconds(5);
        fixture.Hosts.Fixture.Clock.Now += TimeSpan.FromSeconds(61);
        fixture.Transport.AfterCommit = async (_, response, ct) =>
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            paused.SetResult();
            await finish.Task.WaitAsync(ct);
        };
        var staleWorker = fixture.DeliverAsync(source: true);
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal("Leased", await OutboxAsync(fixture, snapshot.EventId));
        fixture.Transport.AfterCommit = null;
        fixture.Transport.LeaseClock.Now += TimeSpan.FromSeconds(31);
        try
        {
            Assert.True(await fixture.DeliverAsync(source: true));
            Assert.Equal("AwaitingAcknowledgement", await OutboxAsync(fixture, snapshot.EventId));
        }
        finally
        {
            finish.TrySetResult();
        }
        Assert.True(await staleWorker);
        Assert.Equal("AwaitingAcknowledgement", await OutboxAsync(fixture, snapshot.EventId));
        Assert.Equal(3, await fixture.SourceScalarAsync(
            $"SELECT attempts FROM public.entitlement_outbox WHERE event_id = '{snapshot.EventId:D}'"));
    }

    internal static async Task DiscardAcknowledgementResponseAsync(P0213Fixture fixture, EntitlementSnapshotV1 snapshot)
    {
        fixture.Transport.AfterCommit = (request, response, _) =>
        {
            Assert.Equal(EntitlementSnapshotsV1.AcknowledgementPath, request.RequestUri!.AbsolutePath);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            throw new HttpRequestException("Local fixture discarded committed acknowledgement response.");
        };
        Assert.True(await fixture.DeliverAsync(source: false));
        fixture.Transport.AfterCommit = null;
        Assert.Equal("Applied", await OutboxAsync(fixture, snapshot.EventId));
        Assert.Equal("Pending", await MerchantOutboxAsync(fixture, snapshot.EventId));
        fixture.Transport.LeaseClock.Now += TimeSpan.FromSeconds(5);
        Assert.True(await fixture.DeliverAsync(source: false));
        Assert.Equal("Delivered", await MerchantOutboxAsync(fixture, snapshot.EventId));
        Assert.Equal(1L, await fixture.SourceScalarAsync($"""
            SELECT count(*) FROM public.entitlement_merchant_inbox WHERE event_id = '{snapshot.EventId:D}'
            """));
    }

    internal static Task<object?> MerchantOutboxAsync(P0213Fixture fixture, Guid id) =>
        fixture.MerchantScalarAsync($"SELECT status FROM public.entitlement_outbox WHERE event_id = '{id:D}'");

    internal static Task<object?> EvidenceTimesAsync(P0213Fixture fixture, Guid eventId) =>
        fixture.MerchantScalarAsync($"""
            SELECT jsonb_build_object('accountUpstreamVerifiedAt', account_upstream_verified_at,
                'shopUpstreamVerifiedAt', shop_upstream_verified_at, 'issuedAt', issued_at, 'receivedAt', received_at,
                'appliedAt', applied_at)::text FROM public.license_grant_evidence WHERE event_id = '{eventId:D}'
            """);
}
