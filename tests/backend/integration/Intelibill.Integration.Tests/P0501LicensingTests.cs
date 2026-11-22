using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0501LicensingTests
{
    /// <summary>
    /// The observable demonstration: a head verified 2026-11-20 12:00 UTC bounds every permit of a
    /// lease reserved under it at 2026-11-22 12:00 UTC, whatever is read or renewed afterwards.
    /// </summary>
    [Fact]
    public async Task Completed_snapshot_issues_from_the_original_verified_grant()
    {
        var verified = At("2026-11-20T12:00:00Z");
        var deadline = At("2026-11-22T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, end: verified.AddDays(30));

        _clock.Now = At("2026-11-22T11:59:00Z");
        var (lease, captured) = await ReserveAsync(host, actor);
        var grant = Issued(captured);
        var first = Issued(await IssueAsync(host, actor, grant.GrantId, await SnapshotAsync(host, actor, grant.GrantId, lease)));

        Assert.Equal((_clock.Now, deadline, OfflineLicencePermitPolicy.AuthorityFreshness),
            (first.NotBefore, first.ValidUntil, first.LimitingReason));
        await using (var db = Open(Migrator))
        {
            var stored = await db.Set<OfflineLicensePermit>().AsNoTracking().SingleAsync(x => x.ShopId == actor.Shop, Ct);
            var original = await db.Set<OfflineLicenseGrant>().AsNoTracking().SingleAsync(x => x.ShopId == actor.Shop, Ct);
            var key = await db.Set<OfflineLicenseVerificationKey>().AsNoTracking().SingleAsync(x => x.ShopId == actor.Shop, Ct);
            Assert.Equal((first.PermitId, grant.GrantId, lease, actor.User, actor.Session, Device, deadline, first.SignedPermit),
                (stored.Id, stored.GrantId, stored.LeaseId, stored.UserId, stored.SessionId, stored.DeviceId, stored.ValidUntil,
                    stored.SignedPermit));
            Assert.Equal((1L, 1L, verified, deadline, _clock.Now, lease),
                (original.AccountRevision, original.ShopRevision, original.AuthorityVerifiedAt, original.AuthorityValidUntil,
                    original.DeviceVerifiedAt, original.LeaseId));
            Assert.Equal((_keyA.Id, _keyA.Fingerprint), (key.KeyId, key.Fingerprint));
            _output.WriteLine("Stored permit {0}: lease {1}, original revisions {2}/{3} verified {4:O}, window [{5:O}, {6:O}), " +
                "reason {7}, key {8} sha256 {9}", stored.Id, stored.LeaseId, original.AccountRevision, original.ShopRevision,
                original.AuthorityVerifiedAt, stored.NotBefore, stored.ValidUntil, stored.LimitingReason, key.KeyId, key.Fingerprint);
        }

        var scope = ScopeOf(actor, lease);
        var proven = Issued(await VerifyAsync(host, actor, first.SignedPermit, scope, deadline.AddMicroseconds(-1)));
        Assert.Equal((1L, 1L, verified, deadline), (proven.AccountRevision, proven.ShopRevision, proven.AuthorityVerifiedAt, proven.ValidUntil));
        Assert.Equal("OfflinePermit.OutsideValidity", Code(await VerifyAsync(host, actor, first.SignedPermit, scope, deadline)));

        // A later stock snapshot is new stock evidence, not new licence freshness.
        _clock.Now = At("2026-11-22T11:59:59Z");
        var reread = Issued(await IssueAsync(host, actor, grant.GrantId, await SnapshotAsync(host, actor, grant.GrantId, lease)));
        Assert.Equal((_clock.Now, deadline), (reread.NotBefore, reread.ValidUntil));

        // A more generous head arrives. The old reservation keeps the revision and deadline it was verified under.
        await ApplyAsync(actor.Binding, verified.AddDays(60), revision: 2);
        var afterRenewal = Issued(await IssueAsync(host, actor, grant.GrantId, await SnapshotAsync(host, actor, grant.GrantId, lease)));
        var kept = Issued(await VerifyAsync(host, actor, afterRenewal.SignedPermit, scope, _clock.Now));
        Assert.Equal((deadline, 1L, 2L, verified),
            (afterRenewal.ValidUntil, kept.ShopRevision, kept.CurrentShopRevision, kept.AuthorityVerifiedAt));

        // At the deadline the old reservation yields nothing more and nothing is written.
        _clock.Now = deadline;
        var before = await EvidenceAsync(actor.Shop);
        var late = await IssueAsync(host, actor, grant.GrantId, await SnapshotAsync(host, actor, grant.GrantId, lease));
        Assert.Equal("OfflinePermit.DeadlineReached", Code(late));
        Assert.Equal(before, await EvidenceAsync(actor.Shop));
        _output.WriteLine("At {0:O} the old reservation is refused {1}; evidence rows unchanged {2}", _clock.Now, Code(late), before);

        // Only a new verified reservation uses the confirmed renewal.
        var (renewedLease, recaptured) = await ReserveAsync(host, actor);
        var renewedGrant = Issued(recaptured).GrantId;
        var renewed = Issued(await IssueAsync(host, actor, renewedGrant, await SnapshotAsync(host, actor, renewedGrant, renewedLease)));
        var fresh = Issued(await VerifyAsync(host, actor, renewed.SignedPermit, ScopeOf(actor, renewedLease), _clock.Now));
        Assert.Equal((At("2026-11-24T11:59:59Z"), 2L), (renewed.ValidUntil, fresh.ShopRevision));
    }
}
