using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Application.Features.OfflineSalesSnapshot.DTOs;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0501LicensingTests
{
    /// <summary>
    /// Server-owned capture provenance: genuine empty stream completes and issues; unknown or incomplete
    /// captures fail with zero writes; two reservations in one shop with equal catalogue/counts can only
    /// issue or replay their own originating capture with its original completion instant.
    /// </summary>
    [Fact]
    public async Task Snapshot_capture_binds_empty_incomplete_unknown_and_same_shop_reservations()
    {
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var emptyClient = CreateClient(host);

        // 1. Genuine empty completed stream (shop with zero stock batches).
        var emptyActor = await OpenShopAsync(emptyClient, verified, verified.AddDays(30), goods: false);
        _clock.Now = verified.AddHours(1);
        var (emptyLease, emptyCaptured) = await ReserveAsync(host, emptyActor);
        var emptyGrant = Issued(emptyCaptured).GrantId;
        var emptyDone = await SnapshotAsync(host, emptyActor, emptyGrant, emptyLease);
        Assert.Equal((0, _clock.Now), (emptyDone.BatchCount, emptyDone.CompletedAt));

        // Preserve original completion instant across waits and retries.
        _clock.Now = emptyDone.CompletedAt.AddMinutes(5);
        var emptyPermit = Issued(await IssueAsync(host, emptyActor, emptyGrant, emptyDone));
        var emptyProven = Issued(await VerifyAsync(host, emptyActor, emptyPermit.SignedPermit, ScopeOf(emptyActor, emptyLease), _clock.Now));
        Assert.Equal((emptyDone.CompletedAt, _clock.Now), (emptyProven.SnapshotCompletedAt, emptyProven.NotBefore));
        _clock.Now = emptyDone.CompletedAt.AddMinutes(10);
        Assert.Equal(emptyPermit.SignedPermit, Issued(await IssueAsync(host, emptyActor, emptyGrant, emptyDone)).SignedPermit);

        // 2. Populated shop: unknown/incomplete captures and two reservations with equal catalogue/counts.
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));
        _clock.Now = verified.AddHours(2);
        var (lease1, cap1) = await ReserveAsync(host, actor, "p0501-res-1");
        var grant1 = Issued(cap1).GrantId;
        var (lease2, cap2) = await ReserveAsync(host, actor, "p0501-res-2");
        var grant2 = Issued(cap2).GrantId;

        await AssertUnknownAndIncompleteCapturesAsync(host, actor, grant1, lease1);

        var t1 = verified.AddHours(2).AddMinutes(1);
        _clock.Now = t1;
        var comp1 = await SnapshotAsync(host, actor, grant1, lease1, "p0501-res-1");
        var t2 = t1.AddSeconds(10);
        _clock.Now = t2;
        var comp2 = await SnapshotAsync(host, actor, grant2, lease2, "p0501-res-2");
        Assert.Equal((comp1.CatalogVersion, comp1.BatchCount), (comp2.CatalogVersion, comp2.BatchCount));
        Assert.Equal(1, comp1.BatchCount);

        // Cross-reservation misuse refuses with zero evidence writes.
        await RefusedAsync(actor.Shop, "OfflinePermit.ScopeMismatch", () => IssueAsync(host, actor, grant2, comp1));
        await RefusedAsync(actor.Shop, "OfflinePermit.ScopeMismatch", () => IssueAsync(host, actor, grant1, comp2));
        var relabeled = new OfflineSnapshotCompletion(comp1.SnapshotId, grant2, actor.Shop, actor.User, actor.Session,
            "p0501-res-2", lease2, comp1.CatalogVersion, comp1.BatchCount, comp1.StartedAt, comp1.CompletedAt, Guid.NewGuid());
        await RefusedAsync(actor.Shop, "OfflinePermit.SnapshotIncomplete", () => IssueAsync(host, actor, grant2, relabeled));

        // Only the originating completed capture creates and replays its permit, keeping t1/t2.
        var issue1At = t2.AddSeconds(15);
        _clock.Now = issue1At;
        var permit1 = Issued(await IssueAsync(host, actor, grant1, comp1));
        var proven1 = Issued(await VerifyAsync(host, actor, permit1.SignedPermit, ScopeOf(actor, lease1, "p0501-res-1"), issue1At));
        Assert.Equal((t1, issue1At), (proven1.SnapshotCompletedAt, proven1.NotBefore));

        _clock.Now = issue1At.AddSeconds(30);
        Assert.Equal(permit1.SignedPermit, Issued(await IssueAsync(host, actor, grant1, comp1)).SignedPermit);
        var alteredTime = new OfflineSnapshotCompletion(comp1.SnapshotId, grant1, actor.Shop, actor.User, actor.Session,
            "p0501-res-1", lease1, comp1.CatalogVersion, comp1.BatchCount, comp1.StartedAt, t1.AddSeconds(1), Guid.NewGuid());
        await RefusedAsync(actor.Shop, "OfflinePermit.SnapshotIncomplete", () => IssueAsync(host, actor, grant1, alteredTime));

        var permit2 = Issued(await IssueAsync(host, actor, grant2, comp2));
        var proven2 = Issued(await VerifyAsync(host, actor, permit2.SignedPermit, ScopeOf(actor, lease2, "p0501-res-2"), _clock.Now));
        Assert.Equal((t2, _clock.Now), (proven2.SnapshotCompletedAt, proven2.NotBefore));
    }

    private async Task AssertUnknownAndIncompleteCapturesAsync(ApiWebApplicationFactory host, Actor actor, Guid grant, Guid lease)
    {
        // Never-started capture has null completion -> refused with zero writes.
        OfflineSnapshotCapture unstarted;
        using (AmbientSessionContext.Enter(actor.User, actor.Shop))
        await using (var scope = host.Services.CreateAsyncScope())
            unstarted = scope.ServiceProvider.GetRequiredService<IOfflineLicencePermitIssuer>()
                .StartSnapshotCapture(actor.Claims, grant, lease, "p0501-res-1");
        Assert.Null(unstarted.Completion);
        await RefusedAsync(actor.Shop, "OfflinePermit.SnapshotIncomplete", () => IssueAsync(host, actor, grant, unstarted.Completion));

        // Partially enumerated stream stops after batch commit, before CompleteRecord: lot row exists in DB, yet refused.
        Guid partialSnapshotId;
        long partialCatalog = 0;
        DateTimeOffset partialStartedAt;
        using (AmbientSessionContext.Enter(actor.User, actor.Shop))
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var capture = scope.ServiceProvider.GetRequiredService<IOfflineLicencePermitIssuer>()
                .StartSnapshotCapture(actor.Claims, grant, lease, "p0501-res-1");
            partialSnapshotId = capture.SnapshotId;
            partialStartedAt = capture.StartedAt;
            await foreach (var record in capture.StreamAsync(Ct))
            {
                if (record is OfflineSalesSnapshotMetadataRecord meta) partialCatalog = meta.Metadata.CatalogVersion ?? 0;
                if (record is OfflineSalesSnapshotBatchRecord) break;
            }
            Assert.Null(capture.Completion);
            await RefusedAsync(actor.Shop, "OfflinePermit.SnapshotIncomplete", () => IssueAsync(host, actor, grant, capture.Completion));
        }

        await using (var db = Open(Migrator))
            Assert.True(await db.OfflineExpiryEvidence.AnyAsync(x => x.SnapshotId == partialSnapshotId, Ct));

        var forgedPartial = new OfflineSnapshotCompletion(partialSnapshotId, grant, actor.Shop, actor.User, actor.Session,
            "p0501-res-1", lease, partialCatalog, 1, partialStartedAt, _clock.Now, Guid.NewGuid());
        await RefusedAsync(actor.Shop, "OfflinePermit.SnapshotIncomplete", () => IssueAsync(host, actor, grant, forgedPartial));

        // Fabricated empty completion for a never-started snapshot ID -> refused.
        var forgedEmpty = new OfflineSnapshotCompletion(Guid.NewGuid(), grant, actor.Shop, actor.User, actor.Session,
            "p0501-res-1", lease, partialCatalog, 0, _clock.Now, _clock.Now, Guid.NewGuid());
        await RefusedAsync(actor.Shop, "OfflinePermit.SnapshotIncomplete", () => IssueAsync(host, actor, grant, forgedEmpty));

        // Completion recorded on another host is unknown to this host -> refused.
        using var foreignHost = CreateHost();
        var foreignDone = await SnapshotAsync(foreignHost, actor, grant, lease, "p0501-res-1");
        await RefusedAsync(actor.Shop, "OfflinePermit.SnapshotIncomplete", () => IssueAsync(host, actor, grant, foreignDone));

        // Capture whose lots were recorded before the lease grant's CapturedAt fails chronology -> refused.
        var savedNow = _clock.Now;
        _clock.Now = savedNow.AddMicroseconds(-1);
        var preGrantDone = await SnapshotAsync(host, actor, grant, lease, "p0501-res-1");
        _clock.Now = savedNow;
        await RefusedAsync(actor.Shop, "OfflinePermit.SnapshotIncomplete", () => IssueAsync(host, actor, grant, preGrantDone));
    }

    /// <summary>
    /// Backward clock movement between capture and NotBefore on replay, or backward during signing of a
    /// new permit, refuses with matching OutsideValidity verification semantics and unchanged permit counts.
    /// </summary>
    [Fact]
    public async Task Backward_clock_between_capture_and_issuance_or_during_signing_refuses_permit()
    {
        var verified = At("2026-11-20T12:00:00Z");
        var stepBackDuringSign = false;
        var signStepTarget = default(DateTimeOffset);
        using var host = CreateHost(configure: services =>
        {
            var inner = services.Last(x => x.ServiceType == typeof(IOfflinePermitSigner));
            services.AddSingleton<IOfflinePermitSigner>(provider => new WaitingSigner(
                (IOfflinePermitSigner)inner.ImplementationFactory!(provider),
                () => { if (stepBackDuringSign) _clock.Now = signStepTarget; }));
        });
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));

        var captureAt = verified.AddHours(1);
        _clock.Now = captureAt;
        var (lease, captured) = await ReserveAsync(host, actor);
        var grant = Issued(captured).GrantId;
        var done1 = await SnapshotAsync(host, actor, grant, lease);

        // Separate lot capture (captureAt) from first issuance (firstIssueAt = captureAt + 5m).
        var firstIssueAt = captureAt.AddMinutes(5);
        _clock.Now = firstIssueAt;
        var permit1 = Issued(await IssueAsync(host, actor, grant, done1));
        Assert.Equal(firstIssueAt, permit1.NotBefore);
        var scope = ScopeOf(actor, lease);
        Assert.Equal(captureAt, Issued(await VerifyAsync(host, actor, permit1.SignedPermit, scope, firstIssueAt)).SnapshotCompletedAt);

        // Move time strictly between captureAt and NotBefore, then retry the same completion.
        var between = captureAt.AddMinutes(2);
        _clock.Now = between;
        Assert.Equal("OfflinePermit.OutsideValidity", Code(await VerifyAsync(host, actor, permit1.SignedPermit, scope, between)));
        await RefusedAsync(actor.Shop, "OfflinePermit.OutsideValidity", () => IssueAsync(host, actor, grant, done1));

        // Move time backward during signing of a fresh completion: new insert must roll back.
        _clock.Now = firstIssueAt;
        var done2 = await SnapshotAsync(host, actor, grant, lease);
        var secondIssueAt = firstIssueAt.AddMinutes(1);
        _clock.Now = secondIssueAt;
        signStepTarget = secondIssueAt.AddSeconds(-1);
        stepBackDuringSign = true;
        await RefusedAsync(actor.Shop, "OfflinePermit.OutsideValidity", () => IssueAsync(host, actor, grant, done2));
        Assert.Equal(1, (await EvidenceAsync(actor.Shop)).Permits);
    }
}
