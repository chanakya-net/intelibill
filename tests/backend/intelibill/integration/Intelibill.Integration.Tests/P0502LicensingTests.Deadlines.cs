using Intelibill.Application.Common.Licensing.Offline;
using static Intelibill.Application.Common.Licensing.Offline.OfflineLicencePermitPolicy;

namespace Intelibill.Integration.Tests;

public sealed partial class P0502LicensingTests
{
    /// <summary>
    /// Every HTTP-reachable cap bounds the delivered permit and agrees between the reservation's
    /// licenceEvidence, the bound stream's licencePermit record, the stream's complete record and
    /// server-side signature verification. Strict FiscalYearEnd, DeviceFreshness, SnapshotLifetime
    /// and StockEvidenceExpiry wins cannot be produced through ReserveInvoiceLease (which never
    /// creates a lease outliving its fiscal series and always follows verification); they remain
    /// pinned by tests/contracts/licensing/offline-permit-v1.json and P0501LicensingTests.
    /// </summary>
    [Theory]
    [InlineData(AuthorityFreshness)]
    [InlineData(LicenceEnd)]
    [InlineData("ScheduledSuspension")]
    [InlineData(CurrentLicenceEnd)]
    [InlineData(AuthDeadline)]
    [InlineData(LeaseExpiry)]
    [InlineData(TillDayEnd)]
    [InlineData("FreshnessTie")]
    public async Task Lease_and_snapshot_evidence_agree_for_each_reachable_deadline(string cap)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var scenario = await ArrangeReachableCapAsync(host, client, cap);
        var (actor, leaseId, grantId, grantSha256, keyId) =
            (scenario.Actor, scenario.LeaseId, scenario.GrantId, scenario.GrantSha256, scenario.KeyId);

        var stream = await StreamSnapshotHttpAsync(client, BoundStreamUrl(leaseId, grantId, Device, scenario.Till));
        var record = stream.RequirePermitImmediatelyBeforeComplete();
        var snapshotId = stream.Metadata.GetProperty("snapshotId").GetGuid();
        var completedAt = stream.Complete.GetProperty("completedAt").GetDateTimeOffset();

        Assert.Equal((1, "issued", leaseId, grantId, snapshotId, keyId, _clock.Now, scenario.ValidUntil, scenario.Reason),
            (record.GetProperty("version").GetInt32(),
             record.GetProperty("status").GetString(),
             record.GetProperty("leaseId").GetGuid(),
             record.GetProperty("grantId").GetGuid(),
             record.GetProperty("snapshotId").GetGuid(),
             record.GetProperty("keyId").GetString(),
             record.GetProperty("notBefore").GetDateTimeOffset(),
             record.GetProperty("validUntil").GetDateTimeOffset(),
             record.GetProperty("limitingReason").GetString()));

        var signedToken = record.GetProperty("signedPermit").GetString()!;
        var scope = ScopeOf(actor, leaseId);
        var last = scenario.ValidUntil.AddMicroseconds(-1);
        var proven = Issued(await VerifyAsync(host, actor, signedToken, scope, last));
        Assert.Equal((record.GetProperty("permitId").GetGuid(), grantId, grantSha256, actor.Shop, actor.User, actor.Session,
                Device, leaseId, snapshotId, completedAt, _clock.Now, scenario.ValidUntil, scenario.Reason, scenario.Till),
            (proven.PermitId, proven.GrantId, proven.GrantSha256, proven.ShopId, proven.UserId, proven.SessionId,
                proven.DeviceId, proven.LeaseId, proven.SnapshotId, proven.SnapshotCompletedAt, proven.NotBefore,
                proven.ValidUntil, proven.LimitingReason, proven.TillSessionId));
        Assert.Equal("OfflinePermit.OutsideValidity", Code(await VerifyAsync(host, actor, signedToken, scope, scenario.ValidUntil)));

        // One microsecond before the deadline: still issued for the same exact deadline.
        _clock.Now = last;
        var edgeStream = await StreamSnapshotHttpAsync(client, BoundStreamUrl(leaseId, grantId, Device, scenario.Till));
        var edge = edgeStream.RequirePermitImmediatelyBeforeComplete();
        Assert.Equal(("issued", last, scenario.ValidUntil),
            (edge.GetProperty("status").GetString(), edge.GetProperty("notBefore").GetDateTimeOffset(),
             edge.GetProperty("validUntil").GetDateTimeOffset()));

        // At the deadline itself: refused without writing any evidence row, while the snapshot still completes.
        _clock.Now = scenario.ValidUntil;
        var before = await EvidenceAsync(actor.Shop);
        var atBoundary = await StreamSnapshotHttpAsync(client, BoundStreamUrl(leaseId, grantId, Device, scenario.Till));
        var refused = atBoundary.RequirePermitImmediatelyBeforeComplete();
        Assert.Equal("refused", refused.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(refused.GetProperty("refusalCode").GetString()));
        Assert.Equal(before, await EvidenceAsync(actor.Shop));
        _output.WriteLine("{0}: [{1:O}, {2:O}) reason={3}, boundaryRefusal={4}",
            cap, record.GetProperty("notBefore").GetDateTimeOffset(), scenario.ValidUntil, scenario.Reason,
            refused.GetProperty("refusalCode").GetString());
    }

    private async Task<CapScenario> ArrangeReachableCapAsync(ApiWebApplicationFactory host, HttpClient client, string cap)
    {
        var verified = At(cap == LeaseExpiry ? "2027-03-31T10:00:00Z" : "2026-11-20T12:00:00Z");
        var reserveAt = cap == "FreshnessTie" ? verified : verified.AddHours(1);
        var actor = await OpenShopAsync(client, verified, cap == LicenceEnd ? verified.AddHours(10) : verified.AddDays(30));

        if (cap == "ScheduledSuspension")
            await ApplyAsync(actor.Binding, verified.AddDays(30), revision: 2, restriction: "Trading",
                suspensionEffectiveAt: verified.AddHours(5));
        if (cap == AuthDeadline)
            await PinAuthEndAsync(actor.Session, reserveAt.AddHours(2), reserveAt.AddMinutes(90));

        _clock.Now = reserveAt;
        var till = cap == TillDayEnd ? await OpenTillAsync(host, actor) : (Guid?)null;
        var lease = await ReserveHttpAsync(client);
        var leaseId = lease.GetProperty("leaseId").GetGuid();
        var evidence = RequireLicenceEvidence(lease);
        var grantId = evidence.GetProperty("grantId").GetGuid();
        var grantSha256 = evidence.GetProperty("grantSha256").GetString()!;
        var keyId = evidence.GetProperty("keyId").GetString()!;

        if (cap == CurrentLicenceEnd)
        {
            _clock.Now = reserveAt.AddMinutes(1);
            await ApplyAsync(actor.Binding, reserveAt.AddHours(3), revision: 2);
        }
        if (cap == AuthDeadline)
            await PinAuthEndAsync(actor.Session, reserveAt.AddDays(30));

        var (validUntil, reason) = cap switch
        {
            LicenceEnd => (verified.AddHours(10), LicenceEnd),
            "ScheduledSuspension" => (verified.AddHours(5), LicenceEnd),
            CurrentLicenceEnd => (reserveAt.AddHours(3), CurrentLicenceEnd),
            AuthDeadline => (reserveAt.AddMinutes(90), AuthDeadline),
            // Production ReserveInvoiceLease caps ExpiresAt at 1 April 00:00 IST (31 March 18:30 UTC),
            // so LeaseExpiry ties FiscalYearEnd and wins by tie order (contract vector lease-ends-with-fiscal-year).
            LeaseExpiry => (At("2027-03-31T18:30:00Z"), LeaseExpiry),
            TillDayEnd => (At("2026-11-20T18:30:00Z"), TillDayEnd),
            _ => (verified.AddHours(48), AuthorityFreshness),
        };
        return new(actor, leaseId, grantId, grantSha256, keyId, till, validUntil, reason);
    }

    private sealed record CapScenario(
        Actor Actor, Guid LeaseId, Guid GrantId, string GrantSha256, string KeyId,
        Guid? Till, DateTimeOffset ValidUntil, string Reason);
}
