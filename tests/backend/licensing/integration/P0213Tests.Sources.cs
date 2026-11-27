using System.Net;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Policies;
using Intelibill.Shared.Contracts.V1;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0213Tests
{
    [Fact]
    public async Task EverySourcePublishesCompleteStateAndReconciliationCoversBothScopes()
    {
        await using var fixture = await P0213Fixture.StartAsync(database);
        var first = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var second = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var unnamed = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(first.Client);
        await P0213Fixture.LinkAsync(account, first, first);
        await P0213Fixture.LinkAsync(account, first, second);
        // An existing affiliation with no head is picked up by the bounded licensing bootstrap scan.
        await fixture.Trial.LinkAsync(account, first.UserId, unnamed);
        await fixture.ScanAsync(source: true);
        Assert.Equal("PendingActivation", (await fixture.LatestSnapshotAsync(unnamed.ShopId)).Shop!.State);

        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(first, fixture.Trial.Accept())).Status);
        await ExtendTrialAsync(fixture, account, first.ShopId);
        var extended = await fixture.LatestSnapshotAsync(first.ShopId);
        Assert.Equal(Utc("2027-03-10T13:15:30.123456Z"), extended.Shop!.AccessEndsAt);
        await AssertGroupedGrantAsync(fixture, account, first.ShopId, second.ShopId, unnamed.ShopId);
        await AssertReconciliationAsync(fixture, account, first, second, extended.Account!.Revision);
    }

    private static async Task ExtendTrialAsync(P0213Fixture fixture, Guid account, Guid shop)
    {
        var path = $"/api/admin/accounts/{account:D}/shops/{shop:D}/trial-extensions";
        var manager = fixture.Trial.CatalogueManager!;
        const string end = "2027-03-10T13:15:30.123456Z";
        const string reason = "Fixture evaluation was delayed";
        const string reference = "FIXTURE-859";
        using var previewResponse = await P0212Fixture.PostAsync(manager, path + "/preview",
            new { endsAt = end, reason, reviewReference = reference });
        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        var preview = await P0206Fixture.BodyAsync(previewResponse);
        var operation = new
        {
            operationKey = Guid.NewGuid(), endsAt = end, reason, reviewReference = reference,
            currentVersion = preview.GetProperty("currentVersion").GetInt64(),
            sourceHash = preview.GetProperty("sourceHash").GetString(), confirmed = true,
        };
        using var created = await P0212Fixture.PostAsync(manager, path, operation);
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var count = await fixture.Trial.CountAsync("entitlement_snapshots");
        using var replay = await P0212Fixture.PostAsync(manager, path, operation);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(count, await fixture.Trial.CountAsync("entitlement_snapshots"));
    }

    private static async Task AssertGroupedGrantAsync(P0213Fixture fixture, Guid account, Guid first, Guid second, Guid unnamed)
    {
        var snapshotCount = await fixture.Trial.CountAsync("entitlement_snapshots");
        var before = await fixture.LatestSnapshotAsync(unnamed);
        var start = Utc("2027-02-18T18:30:00Z");
        var end = Utc("2027-03-31T18:30:00Z");
        var draft = P0212Fixture.Draft([first, second], start, end);
        var manager = fixture.Trial.CatalogueManager!;
        var preview = await P0212Fixture.PreviewAsync(manager, account, draft);
        var operation = P0212Fixture.Confirm(preview, Guid.NewGuid());
        using var result = await P0212Fixture.PostAsync(manager, P0212Fixture.Base(account), operation);
        Assert.True(result.StatusCode == HttpStatusCode.Created, await result.Content.ReadAsStringAsync());
        using var replay = await P0212Fixture.PostAsync(manager, P0212Fixture.Base(account), operation);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(snapshotCount + 2, await fixture.Trial.CountAsync("entitlement_snapshots"));

        var active = await fixture.LatestSnapshotAsync(first);
        Assert.Equal("Trial", active.Shop!.Basis);
        Assert.Equal(start, active.Shop.NextBoundary);
        var scheduled = await fixture.LatestSnapshotAsync(second);
        Assert.Equal("Scheduled", scheduled.Shop!.State);
        Assert.Equal("Complimentary", scheduled.Shop.Basis);
        Assert.Equal(before, await fixture.LatestSnapshotAsync(unnamed));
    }

    private async Task AssertReconciliationAsync(P0213Fixture fixture, Guid account, Merchant first, Merchant second, long oldRevision)
    {
        // Accepted local consent queues one reconciliation even if no source delivery has happened yet.
        await fixture.ScanAsync(source: false);
        await fixture.ScanAsync(source: false);
        Assert.Equal(2L, await fixture.MerchantScalarAsync($"""
            SELECT count(*) FROM public.entitlement_messages WHERE kind = 'Reconciliation' AND account_id = '{account:D}'
            """));
        var start = Utc("2027-02-18T18:30:00Z");
        fixture.Hosts.StatusClock.Now = start.AddTicks(-10);
        await fixture.DrainAsync(source: false);
        await fixture.DrainAsync(source: true);
        var before = await HeadsAsync(second.ShopId, account);
        Assert.Equal(LicenseProjectionDecision.Incomplete, Evaluate(before.Item1, before.Item2, start.AddTicks(-10)));
        Assert.Equal(LicenseProjectionDecision.VerificationRequired, Evaluate(before.Item1, before.Item2, start));

        fixture.Hosts.StatusClock.Now = start;
        await fixture.ScanAsync(source: true);
        await fixture.DrainAsync(source: true);
        var firstHead = await HeadsAsync(first.ShopId, account);
        Assert.Equal("Trial", firstHead.Item1.Basis);
        var secondHead = await HeadsAsync(second.ShopId, account);
        Assert.Equal("Complimentary", secondHead.Item1.Basis);
        Assert.Equal(start, secondHead.Item1.UpstreamVerifiedAt);
        Assert.Equal(start, secondHead.Item2.UpstreamVerifiedAt);
        Assert.True(secondHead.Item2.Revision > oldRevision);
        Assert.Equal(LicenseProjectionDecision.VerifiedPermission, Evaluate(secondHead.Item1, secondHead.Item2, start));
        Assert.Equal(LicenseProjectionDecision.VerifiedPermission, Evaluate(secondHead.Item1, secondHead.Item2, start.AddTicks(10)));
    }
}
