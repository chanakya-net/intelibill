using System.Net;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0213Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0214Tests
{
    [Fact]
    public async Task LostAcknowledgementReplaysAndConvergesWithoutRegranting()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var (owner, account) = await LinkAndConfirmAsync(fixture);
        var manager = fixture.Trial.CatalogueManager!;
        await SaveGrantAsync(fixture, manager, account, owner.ShopId);
        fixture.Hosts.Fixture.Clock.Now += TimeSpan.FromSeconds(1);
        var applyTime = EntitlementSnapshotsV1.Instant(fixture.AuthClock);
        var snapshot = await fixture.LatestSnapshotAsync(owner.ShopId);
        Assert.True(await fixture.DeliverAsync(source: true));
        var facts = await ReadGrantEvidenceAsync(fixture, snapshot);

        await LoseAcknowledgementAsync(fixture, snapshot);
        var waiting = await BothAsync(owner, manager, account, "Pending", "AwaitingAcknowledgement");
        Assert.Equal(snapshot.Shop!.Revision - 1, waiting.GetProperty("appliedShopRevision").GetInt64());
        Assert.True(waiting.GetProperty("lastSynchronizedAt").GetDateTimeOffset() < applyTime);

        fixture.Hosts.Fixture.Clock.Now += TimeSpan.FromSeconds(61);
        fixture.Transport.LeaseClock.Now += TimeSpan.FromSeconds(4) - TimeSpan.FromMicroseconds(1);
        Assert.False(await fixture.DeliverAsync(source: true));
        Assert.False(await fixture.DeliverAsync(source: false));
        fixture.Transport.LeaseClock.Now += TimeSpan.FromMicroseconds(1);
        Assert.True(await fixture.DeliverAsync(source: true));
        await DiscardAcknowledgementResponseAsync(fixture, snapshot, retrySeconds: 8);

        var applied = await BothAsync(owner, manager, account, "Applied", "Acknowledged");
        Assert.Equal(applyTime, applied.GetProperty("lastSynchronizedAt").GetDateTimeOffset());
        Assert.Equal(snapshot.Shop.UpstreamVerifiedAt, applied.GetProperty("savedVerifiedAt").GetDateTimeOffset());
        Assert.Equal(facts, await ReadGrantEvidenceAsync(fixture, snapshot));
    }

    [Fact]
    public async Task UnresolvedSynchronizationReadsNeedsReviewAndResynchronizationConverges()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var (owner, account) = await LinkAndConfirmAsync(fixture);
        var manager = fixture.Trial.CatalogueManager!;
        await SaveGrantAsync(fixture, manager, account, owner.ShopId);
        var snapshot = await fixture.LatestSnapshotAsync(owner.ShopId);
        await ExhaustSnapshotsAsync(fixture);
        var dead = await BothAsync(owner, manager, account, "NeedsReview", "DeadLetter");
        Assert.Equal("Unavailable", dead.GetProperty("attention").GetString());
        Assert.Equal(snapshot.Shop!.Revision - 1, dead.GetProperty("appliedShopRevision").GetInt64());

        await SendReceiptAsync(fixture, snapshot);
        await fixture.DrainAsync(source: false);
        var late = await BothAsync(owner, manager, account, "Applied", "Acknowledged");
        Assert.Equal(snapshot.Shop.Revision, late.GetProperty("appliedShopRevision").GetInt64());

        var underReview = await ScanAtBoundaryAsync(fixture, snapshot);
        await BothAsync(owner, manager, account, "Pending", "Queued");
        var ack = await SendReceiptAsync(fixture, underReview);
        await SendAcknowledgementAsync(fixture, ack with { Outcome = "NeedsReview", Disposition = "ConflictingShopScope" });
        var review = await BothAsync(owner, manager, account, "NeedsReview", "Acknowledged");
        Assert.Equal("ConflictingShopScope", review.GetProperty("attention").GetString());
        Assert.Equal(late.GetProperty("lastSynchronizedAt").GetDateTimeOffset(), review.GetProperty("lastSynchronizedAt").GetDateTimeOffset());

        var replacement = await ScanAtBoundaryAsync(fixture, underReview);
        await fixture.DrainAsync(source: true);
        await SendAcknowledgementAsync(fixture, await SendReceiptAsync(fixture, replacement));
        var result = await BothAsync(owner, manager, account, "Applied", "Acknowledged");
        Assert.True(result.GetProperty("appliedAccountRevision").GetInt64() > snapshot.Account!.Revision);
        Assert.True(result.GetProperty("appliedShopRevision").GetInt64() > snapshot.Shop.Revision);
        Assert.Equal(1L, await fixture.Trial.CountAsync("complimentary_grants"));
    }

    [Fact]
    public async Task MerchantReconciliationRefreshesBothScopes()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var owner = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(owner.Client);
        await P0213Fixture.LinkAsync(account, owner, owner);
        var original = await fixture.LatestSnapshotAsync(owner.ShopId);

        fixture.Hosts.StatusClock.Now += TimeSpan.FromHours(1);
        await fixture.ScanAsync(source: false);
        await fixture.DrainAsync(source: false);
        var pending = await BothAsync(owner, fixture.Trial.CatalogueManager!, account, "Pending", "Queued");
        Assert.Equal(original.Account!.Revision + 1, pending.GetProperty("savedAccountRevision").GetInt64());
        Assert.Equal(original.Shop!.Revision + 1, pending.GetProperty("savedShopRevision").GetInt64());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("lastSynchronizedAt").ValueKind);

        await fixture.DrainAsync(source: true);
        await fixture.DrainAsync(source: false);
        var applied = await BothAsync(owner, fixture.Trial.CatalogueManager!, account, "Applied", "Acknowledged");
        Assert.Equal(original.Account.Revision + 1, applied.GetProperty("appliedAccountRevision").GetInt64());
        Assert.Equal(original.Shop.Revision + 1, applied.GetProperty("appliedShopRevision").GetInt64());
    }

    private static async Task<EntitlementSnapshotV1> ScanAtBoundaryAsync(P0213Fixture fixture, EntitlementSnapshotV1 snapshot)
    {
        var due = (DateTime)(await fixture.SourceScalarAsync(
            $"SELECT next_verification_at FROM entitlement_shop_heads WHERE shop_id = '{snapshot.ShopId:D}'"))!;
        fixture.Hosts.StatusClock.Now = new DateTimeOffset(due).AddTicks(-10);
        await fixture.ScanAsync(source: true);
        Assert.Equal(snapshot.EventId, (await fixture.LatestSnapshotAsync(snapshot.ShopId)).EventId);
        fixture.Hosts.StatusClock.Now = new DateTimeOffset(due);
        await fixture.ScanAsync(source: true);
        var replacement = await fixture.LatestSnapshotAsync(snapshot.ShopId);
        Assert.Equal(snapshot.Shop!.Revision + 1, replacement.Shop!.Revision);
        return replacement;
    }

    internal static async Task LoseAcknowledgementAsync(P0213Fixture fixture, EntitlementSnapshotV1 snapshot)
    {
        fixture.Transport.BeforeSend = request =>
        {
            Assert.Equal(EntitlementSnapshotsV1.AcknowledgementPath, request.RequestUri!.AbsolutePath);
            throw new HttpRequestException("Local fixture dropped acknowledgement before receipt.");
        };
        try { Assert.True(await fixture.DeliverAsync(source: false)); }
        finally { fixture.Transport.BeforeSend = null; }
        Assert.Equal("Pending", await MerchantOutboxAsync(fixture, snapshot.EventId));
        Assert.Equal(0L, await fixture.SourceScalarAsync(
            $"SELECT count(*) FROM entitlement_merchant_inbox WHERE event_id = '{snapshot.EventId:D}'"));
    }

    internal static async Task ExhaustSnapshotsAsync(P0213Fixture fixture)
    {
        fixture.Transport.BeforeSend = _ => throw new HttpRequestException("Local fixture transport unavailable.");
        try
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                Assert.True(await fixture.DeliverAsync(source: true));
                fixture.Transport.LeaseClock.Now += TimeSpan.FromMinutes(5);
            }
        }
        finally { fixture.Transport.BeforeSend = null; }
    }

    private static async Task SendAcknowledgementAsync(P0213Fixture fixture, EntitlementAcknowledgementV1 ack)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(ack, EntitlementSnapshotsV1.Json);
        using var request = await fixture.Transport.SignedAsync(bytes, fixture.AuthClock, merchant: true,
            path: EntitlementSnapshotsV1.AcknowledgementPath);
        using var response = await fixture.LicensingApi.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<(object? Times, object? ShopHead, long Evidence, long Grants, long Snapshots)> ReadGrantEvidenceAsync(
        P0213Fixture fixture, EntitlementSnapshotV1 snapshot) =>
        (await EvidenceTimesAsync(fixture, snapshot.EventId),
            await fixture.MerchantScalarAsync($"""
                SELECT jsonb_build_array(revision, evidence_id)::text FROM shop_license_states
                WHERE shop_id = '{snapshot.ShopId:D}' AND enforcement_activated_at IS NULL
                """) ?? throw new InvalidOperationException("Missing observe-only head."),
            await EvidenceCountAsync(fixture, snapshot.ShopId), await fixture.Trial.CountAsync("complimentary_grants"),
            await fixture.Trial.CountAsync("entitlement_snapshots"));
}
