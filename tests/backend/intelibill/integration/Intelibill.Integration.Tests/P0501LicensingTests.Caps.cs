using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static Intelibill.Application.Common.Licensing.Offline.OfflineLicencePermitPolicy;

namespace Intelibill.Integration.Tests;

public sealed partial class P0501LicensingTests
{
    /// <summary>
    /// Each cap the stored server facts can make the earliest, on PostgreSQL. Device freshness, snapshot
    /// lifetime and stock-evidence expiry can only tie or trail the authority's own 48 hours for facts
    /// this server writes (reservation follows verification; capture follows reservation), so their
    /// strict wins are pinned by the contract vectors and their tie order here.
    /// </summary>
    [Theory]
    [InlineData(AuthorityFreshness)]
    [InlineData(LicenceEnd)]
    [InlineData("ScheduledSuspension")]
    [InlineData(CurrentLicenceEnd)]
    [InlineData(AuthDeadline)]
    [InlineData(LeaseExpiry)]
    [InlineData(FiscalYearEnd)]
    [InlineData(TillDayEnd)]
    [InlineData("FreshnessTie")]
    public async Task Each_server_cap_bounds_the_half_open_window(string cap)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var scenario = await ArrangeCapAsync(host, client, cap);
        var (actor, lease, grant) = (scenario.Actor, scenario.Lease, scenario.Grant);

        var permit = Issued(await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, grant, lease), scenario.Till));
        Assert.Equal((_clock.Now, scenario.ValidUntil, scenario.Reason), (permit.NotBefore, permit.ValidUntil, permit.LimitingReason));

        // Half-open: usable up to, never at, the deadline.
        var scope = ScopeOf(actor, lease);
        var last = scenario.ValidUntil.AddMicroseconds(-1);
        var proven = Issued(await VerifyAsync(host, actor, permit.SignedPermit, scope, last));
        Assert.Equal((scenario.ValidUntil, scenario.Till), (proven.ValidUntil, proven.TillSessionId));
        Assert.Equal("OfflinePermit.OutsideValidity", Code(await VerifyAsync(host, actor, permit.SignedPermit, scope, scenario.ValidUntil)));
        Assert.Equal("OfflinePermit.OutsideValidity",
            Code(await VerifyAsync(host, actor, permit.SignedPermit, scope, permit.NotBefore.AddMicroseconds(-1))));

        _clock.Now = last;
        var final = Issued(await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, grant, lease), scenario.Till));
        Assert.Equal((last, scenario.ValidUntil), (final.NotBefore, final.ValidUntil));

        _clock.Now = scenario.ValidUntil;
        var before = await EvidenceAsync(actor.Shop);
        var refused = await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, grant, lease), scenario.Till);
        Assert.True(refused.IsError, "A permit was issued at its own deadline.");
        Assert.Equal(before, await EvidenceAsync(actor.Shop));
        _output.WriteLine("{0}: window [{1:O}, {2:O}) reason {3}; at the deadline issuance answers {4}", cap, permit.NotBefore,
            permit.ValidUntil, permit.LimitingReason, Code(refused));
    }

    /// <summary>
    /// A session that outlives its newest refresh credential (e.g. after refresh lifetime shortening)
    /// is capped by the credential while usable and refused for capture, first issuance and replay at expiry.
    /// </summary>
    [Fact]
    public async Task Refresh_credential_expiry_caps_and_refuses_even_when_session_outlives_it()
    {
        var verified = At("2026-11-20T12:00:00Z");
        var reserveAt = verified.AddHours(1);
        var sessionEnd = reserveAt.AddDays(30);
        var shorterRefreshEnd = reserveAt.AddHours(2);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));

        // Capture the grant while both session and refresh credential end 30 days out, then shorten the credential.
        await PinAuthEndAsync(actor.Session, sessionEnd, sessionEnd);
        _clock.Now = reserveAt;
        var (lease, captured) = await ReserveAsync(host, actor);
        var grant = Issued(captured).GrantId;
        var initialDone = await SnapshotAsync(host, actor, grant, lease);
        var overlongPermit = Issued(await IssueAsync(host, actor, grant, initialDone));
        Assert.Equal(verified.AddHours(48), overlongPermit.ValidUntil);

        // Shorten the current refresh credential while leaving auth_sessions.expires_at 30 days ahead.
        await PinAuthEndAsync(actor.Session, sessionEnd, shorterRefreshEnd);
        await RefusedAsync(actor.Shop, "OfflinePermit.Tightened", () => IssueAsync(host, actor, grant, initialDone));

        var cappedDone = await SnapshotAsync(host, actor, grant, lease);
        var capped = Issued(await IssueAsync(host, actor, grant, cappedDone));
        Assert.Equal((shorterRefreshEnd, AuthDeadline), (capped.ValidUntil, capped.LimitingReason));

        _clock.Now = shorterRefreshEnd.AddMicroseconds(-1);
        Assert.Equal(capped.SignedPermit, Issued(await IssueAsync(host, actor, grant, cappedDone)).SignedPermit);
        var lastMoment = Issued(await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, grant, lease)));
        Assert.Equal((_clock.Now, shorterRefreshEnd, AuthDeadline),
            (lastMoment.NotBefore, lastMoment.ValidUntil, lastMoment.LimitingReason));

        // At refresh expiry, the session still has 30 days left, yet capture, first issuance and replay all refuse.
        _clock.Now = shorterRefreshEnd;
        var freshAtExpiry = await SnapshotAsync(host, actor, grant, lease);
        await RefusedAsync(actor.Shop, "Auth.SessionInvalidated", async () => (await ReserveAsync(host, actor, "p0501-after-refresh")).Grant);
        await RefusedAsync(actor.Shop, "Auth.SessionInvalidated", () => IssueAsync(host, actor, grant, freshAtExpiry));
        await RefusedAsync(actor.Shop, "Auth.SessionInvalidated", () => IssueAsync(host, actor, grant, cappedDone));
        _output.WriteLine("Refresh credential cap: sessionEnd={0:O}, refreshEnd={1:O}; capture/issue/replay refused at refreshEnd",
            sessionEnd, shorterRefreshEnd);
    }

    /// <summary>When tills are disabled, an unverified till session ID is refused instead of entering signed evidence.</summary>
    [Fact]
    public async Task Disabled_tills_refuse_unrelated_till_session_id()
    {
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var otherClient = CreateClient(host);
        var other = await OpenShopAsync(otherClient, verified, verified.AddDays(30));
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));

        _clock.Now = verified.AddHours(1);
        var otherShopTill = await OpenTillAsync(host, other);
        var (lease, captured) = await ReserveAsync(host, actor);
        var grant = Issued(captured).GrantId;
        var done = await SnapshotAsync(host, actor, grant, lease);

        await RefusedAsync(actor.Shop, "OfflinePermit.ScopeMismatch", () => IssueAsync(host, actor, grant, done, Guid.NewGuid()));
        await RefusedAsync(actor.Shop, "OfflinePermit.ScopeMismatch", () => IssueAsync(host, actor, grant, done, otherShopTill));

        // Without a till ID, disabled-till issuance succeeds with a null till in both signed bytes and stored row.
        var issued = Issued(await IssueAsync(host, actor, grant, done));
        var proven = Issued(await VerifyAsync(host, actor, issued.SignedPermit, ScopeOf(actor, lease), _clock.Now));
        Assert.Null(proven.TillSessionId);
        await using var db = Open(Migrator);
        var storedTill = await db.Set<OfflineLicensePermit>().Where(x => x.Id == issued.PermitId)
            .Select(x => x.TillSessionId).SingleAsync(Ct);
        Assert.Null(storedTill);
    }

    private async Task<CapScenario> ArrangeCapAsync(ApiWebApplicationFactory host, HttpClient client, string cap)
    {
        var verified = At(cap == FiscalYearEnd ? "2027-03-31T10:00:00Z" : "2026-11-20T12:00:00Z");
        var reserveAt = cap == "FreshnessTie" ? verified : verified.AddHours(1);
        var actor = await OpenShopAsync(client, verified, cap == LicenceEnd ? verified.AddHours(10) : verified.AddDays(30));
        if (cap == "ScheduledSuspension")
            await ApplyAsync(actor.Binding, verified.AddDays(30), revision: 2, restriction: "Trading",
                suspensionEffectiveAt: verified.AddHours(5));
        if (cap == AuthDeadline)
            await PinAuthEndAsync(actor.Session, reserveAt.AddHours(2), reserveAt.AddMinutes(90));

        _clock.Now = reserveAt;
        var till = cap == TillDayEnd ? await OpenTillAsync(host, actor) : (Guid?)null;
        var (lease, captured) = await ReserveAsync(host, actor, leaseExpiresAt: cap switch
        {
            LeaseExpiry => reserveAt.AddHours(3),
            // A lease that would outlive its series still ends with the fiscal year.
            FiscalYearEnd => reserveAt.AddDays(7),
            _ => null,
        });
        var grant = Issued(captured).GrantId;

        if (cap == CurrentLicenceEnd)
        {
            // A newer, still operating head ends sooner than the one the lease was reserved under.
            _clock.Now = reserveAt.AddMinutes(1);
            await ApplyAsync(actor.Binding, reserveAt.AddHours(3), revision: 2);
        }
        if (cap == AuthDeadline)
            // Extending the session later does not move the end frozen at reservation.
            await PinAuthEndAsync(actor.Session, reserveAt.AddDays(30));

        var (validUntil, reason) = cap switch
        {
            LicenceEnd => (verified.AddHours(10), LicenceEnd),
            "ScheduledSuspension" => (verified.AddHours(5), LicenceEnd),
            CurrentLicenceEnd => (reserveAt.AddHours(3), CurrentLicenceEnd),
            AuthDeadline => (reserveAt.AddMinutes(90), AuthDeadline),
            LeaseExpiry => (reserveAt.AddHours(3), LeaseExpiry),
            FiscalYearEnd => (At("2027-03-31T18:30:00Z"), FiscalYearEnd),
            TillDayEnd => (At("2026-11-20T18:30:00Z"), TillDayEnd),
            _ => (verified.AddHours(48), AuthorityFreshness),
        };
        return new(actor, lease, grant, till, validUntil, reason);
    }

    /// <summary>Switches till sessions on for the shop and opens the owner's drawer for the clock's business day.</summary>
    private async Task<Guid> OpenTillAsync(ApiWebApplicationFactory host, Actor actor, int daysAgo = 0)
    {
        var rollout = host.Services.GetRequiredService<IOptions<TillSessionOptions>>().Value;
        rollout.Enabled = true;
        rollout.AllowedShopIds = [.. rollout.AllowedShopIds, actor.Shop];

        await using var db = Open(Migrator);
        if (await db.Set<ShopTillSetting>().AnyAsync(x => x.ShopId == actor.Shop, Ct))
            await db.Database.ExecuteSqlAsync($"UPDATE shop_till_settings SET enabled = true WHERE shop_id = {actor.Shop}", Ct);
        else
            db.Add(ShopTillSetting.Create(actor.Shop, enabled: true));

        var now = _clock.Now;
        var zone = await db.Shops.Where(x => x.Id == actor.Shop).Select(x => x.BusinessTimeZoneId).SingleAsync(Ct);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime).AddDays(-daysAgo);
        var till = Till.Create(actor.Shop, $"Front {Guid.NewGuid():N}"[..20], actor.User, now).Value;
        var session = TillSession.Open(actor.Shop, till.Id, actor.User, till.Name, "Fixture Owner", actor.User, now, date, zone,
            0m, null).Value;
        db.AddRange(till, session);
        await db.SaveChangesAsync(Ct);
        return session.Id;
    }

    private sealed record CapScenario(Actor Actor, Guid Lease, Guid Grant, Guid? Till, DateTimeOffset ValidUntil, string Reason);
}
