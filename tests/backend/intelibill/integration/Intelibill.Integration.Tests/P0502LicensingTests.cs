using System.Text.Json;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0502LicensingTests
{
    [Fact]
    public async Task Reservation_returns_versioned_evidence_bound_to_the_lease()
    {
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));

        _clock.Now = verified.AddHours(1);
        var lease = await ReserveHttpAsync(client);
        var leaseId = lease.GetProperty("leaseId").GetGuid();
        var evidence = RequireLicenceEvidence(lease);

        Assert.Equal(1, evidence.GetProperty("version").GetInt32());
        var grantId = evidence.GetProperty("grantId").GetGuid();
        var grantSha256 = evidence.GetProperty("grantSha256").GetString()!;
        var keyId = evidence.GetProperty("keyId").GetString()!;
        Assert.Equal(_keyA.Id, keyId);
        Assert.Equal(new Evidence(1, 0, 1), await EvidenceAsync(actor.Shop));

        await using var db = Open(Migrator);
        var grant = await db.Set<OfflineLicenseGrant>().AsNoTracking().SingleAsync(x => x.ShopId == actor.Shop, Ct);
        var key = await db.Set<OfflineLicenseVerificationKey>().AsNoTracking().SingleAsync(x => x.ShopId == actor.Shop, Ct);
        Assert.Equal((grantId, leaseId, actor.User, actor.Session, Device, _keyA.Id, _clock.Now),
            (grant.Id, grant.LeaseId, grant.UserId, grant.SessionId, grant.DeviceId, grant.KeyId, grant.CapturedAt));
        Assert.Equal(OfflinePermitContractV1.Digest(grant.SignedGrant), grantSha256);
        Assert.Equal((_keyA.Id, _keyA.Fingerprint), (key.KeyId, key.Fingerprint));
        _output.WriteLine("Reserved lease {0} bound to grant {1} (sha256 {2}, key {3})", leaseId, grantId, grantSha256, keyId);
    }

    [Fact]
    public async Task Newer_snapshot_alone_never_refreshes_stale_licensing()
    {
        var verified = At("2026-11-20T12:00:00Z");
        var deadline = At("2026-11-22T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));

        _clock.Now = At("2026-11-22T11:59:00Z");
        var lease = await ReserveHttpAsync(client);
        var leaseId = lease.GetProperty("leaseId").GetGuid();
        var evidence = RequireLicenceEvidence(lease);
        var grantId = evidence.GetProperty("grantId").GetGuid();
        var grantSha256 = evidence.GetProperty("grantSha256").GetString()!;

        var firstStream = await StreamSnapshotHttpAsync(client, BoundStreamUrl(leaseId, grantId, Device));
        var first = firstStream.RequirePermitImmediatelyBeforeComplete();
        Assert.Equal(("issued", deadline, OfflineLicencePermitPolicy.AuthorityFreshness),
            (first.GetProperty("status").GetString(), first.GetProperty("validUntil").GetDateTimeOffset(),
             first.GetProperty("limitingReason").GetString()));
        var firstVerified = Issued(await VerifyAsync(host, actor, first.GetProperty("signedPermit").GetString(),
            ScopeOf(actor, leaseId), deadline.AddMicroseconds(-1)));
        Assert.Equal((grantSha256, firstStream.Complete.GetProperty("completedAt").GetDateTimeOffset()),
            (firstVerified.GrantSha256, firstVerified.SnapshotCompletedAt));

        // A later stock snapshot produces new stock evidence, never a new licence deadline.
        _clock.Now = At("2026-11-22T11:59:59Z");
        var secondStream = await StreamSnapshotHttpAsync(client, BoundStreamUrl(leaseId, grantId, Device));
        var second = secondStream.RequirePermitImmediatelyBeforeComplete();
        Assert.NotEqual(firstStream.Metadata.GetProperty("snapshotId").GetGuid(), secondStream.Metadata.GetProperty("snapshotId").GetGuid());
        Assert.Equal(("issued", _clock.Now, deadline),
            (second.GetProperty("status").GetString(), second.GetProperty("notBefore").GetDateTimeOffset(),
             second.GetProperty("validUntil").GetDateTimeOffset()));

        // A more generous head arrives; the existing lease keeps its original verification time and deadline.
        await ApplyAsync(actor.Binding, verified.AddDays(60), revision: 2);
        var thirdStream = await StreamSnapshotHttpAsync(client, BoundStreamUrl(leaseId, grantId, Device));
        var third = thirdStream.RequirePermitImmediatelyBeforeComplete();
        Assert.Equal(("issued", deadline), (third.GetProperty("status").GetString(), third.GetProperty("validUntil").GetDateTimeOffset()));
        var kept = Issued(await VerifyAsync(host, actor, third.GetProperty("signedPermit").GetString(),
            ScopeOf(actor, leaseId), _clock.Now));
        Assert.Equal((1L, 2L, verified, deadline), (kept.ShopRevision, kept.CurrentShopRevision, kept.AuthorityVerifiedAt, kept.ValidUntil));

        // At the deadline the old lease's bound snapshot refuses the permit and writes nothing while still completing stock.
        _clock.Now = deadline;
        var before = await EvidenceAsync(actor.Shop);
        var lateStream = await StreamSnapshotHttpAsync(client, BoundStreamUrl(leaseId, grantId, Device));
        var late = lateStream.RequirePermitImmediatelyBeforeComplete();
        Assert.Equal(("refused", "OfflinePermit.DeadlineReached"),
            (late.GetProperty("status").GetString(), late.GetProperty("refusalCode").GetString()));
        Assert.Equal(before, await EvidenceAsync(actor.Shop));

        // Only a fresh verified reservation adopts revision 2 and gets a new deadline.
        var renewed = await ReserveHttpAsync(client);
        var renewedLeaseId = renewed.GetProperty("leaseId").GetGuid();
        var renewedGrantId = RequireLicenceEvidence(renewed).GetProperty("grantId").GetGuid();
        var renewedStream = await StreamSnapshotHttpAsync(client, BoundStreamUrl(renewedLeaseId, renewedGrantId, Device));
        var renewedPermit = renewedStream.RequirePermitImmediatelyBeforeComplete();
        Assert.Equal(("issued", At("2026-11-24T11:59:59Z")),
            (renewedPermit.GetProperty("status").GetString(), renewedPermit.GetProperty("validUntil").GetDateTimeOffset()));
        var fresh = Issued(await VerifyAsync(host, actor, renewedPermit.GetProperty("signedPermit").GetString(),
            ScopeOf(actor, renewedLeaseId), _clock.Now));
        Assert.Equal(2L, fresh.ShopRevision);
        _output.WriteLine("Old lease kept {0:O} across later snapshot + head rev 2, refused at deadline; new lease got {1:O}",
            deadline, renewedPermit.GetProperty("validUntil").GetDateTimeOffset());
    }

    [Fact]
    public async Task Delivery_shapes_match_the_shared_fixture()
    {
        using var deliveryDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "offline-permit-delivery-v1.json")));
        using var permitDoc = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "offline-permit-v1.json")));
        var fixtureRoot = deliveryDoc.RootElement;
        var permitRoot = permitDoc.RootElement;

        // Cross-check the shared delivery fixture against P05-01's canonical permit fixture.
        var fixtureLeaseEvidence = fixtureRoot.GetProperty("leaseEvidence");
        var fixtureIssued = fixtureRoot.GetProperty("issuedRecord").GetProperty("licencePermit");
        var fixtureRefused = fixtureRoot.GetProperty("refusedRecord").GetProperty("licencePermit");
        Assert.Equal(permitRoot.GetProperty("permit").GetProperty("fields").GetProperty("grantSha256").GetString(),
            fixtureLeaseEvidence.GetProperty("grantSha256").GetString());
        Assert.Equal(permitRoot.GetProperty("permit").GetProperty("token").GetString(),
            fixtureIssued.GetProperty("signedPermit").GetString());

        // Compare live HTTP wire property sets against the shared fixture.
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        await OpenShopAsync(client, verified, verified.AddDays(30));

        _clock.Now = verified.AddHours(1);
        var lease = await ReserveHttpAsync(client);
        var leaseId = lease.GetProperty("leaseId").GetGuid();
        var evidence = RequireLicenceEvidence(lease);
        AssertSamePropertyNames(fixtureLeaseEvidence, evidence);

        var grantId = evidence.GetProperty("grantId").GetGuid();
        var issuedStream = await StreamSnapshotHttpAsync(client, BoundStreamUrl(leaseId, grantId, Device));
        var issuedRecord = issuedStream.RequirePermitImmediatelyBeforeComplete();
        AssertSamePropertyNames(fixtureIssued, issuedRecord);
        Assert.Equal(1, issuedRecord.GetProperty("version").GetInt32());
        Assert.Equal("issued", issuedRecord.GetProperty("status").GetString());

        var refusedStream = await StreamSnapshotHttpAsync(client, BoundStreamUrl(leaseId, Guid.NewGuid(), Device));
        var refusedRecord = refusedStream.RequirePermitImmediatelyBeforeComplete();
        AssertSamePropertyNames(fixtureRefused, refusedRecord);
        Assert.Equal(1, refusedRecord.GetProperty("version").GetInt32());
        Assert.Equal("refused", refusedRecord.GetProperty("status").GetString());
        Assert.Equal("OfflinePermit.GrantNotFound", refusedRecord.GetProperty("refusalCode").GetString());
    }

    private static void AssertSamePropertyNames(JsonElement expected, JsonElement actual)
    {
        var expectedNames = expected.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var actualNames = actual.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedNames, actualNames);
    }
}
