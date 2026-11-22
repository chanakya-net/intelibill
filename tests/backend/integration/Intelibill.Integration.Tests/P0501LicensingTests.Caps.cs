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

        var permit = Issued(await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, lease), scenario.Till));
        Assert.Equal((_clock.Now, scenario.ValidUntil, scenario.Reason), (permit.NotBefore, permit.ValidUntil, permit.LimitingReason));

        // Half-open: usable up to, never at, the deadline.
        var scope = ScopeOf(actor, lease);
        var last = scenario.ValidUntil.AddMicroseconds(-1);
        Assert.Equal(scenario.ValidUntil, Issued(await VerifyAsync(host, actor, permit.SignedPermit, scope, last)).ValidUntil);
        Assert.Equal("OfflinePermit.OutsideValidity", Code(await VerifyAsync(host, actor, permit.SignedPermit, scope, scenario.ValidUntil)));
        Assert.Equal("OfflinePermit.OutsideValidity",
            Code(await VerifyAsync(host, actor, permit.SignedPermit, scope, permit.NotBefore.AddMicroseconds(-1))));

        _clock.Now = last;
        var final = Issued(await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, lease), scenario.Till));
        Assert.Equal((last, scenario.ValidUntil), (final.NotBefore, final.ValidUntil));

        _clock.Now = scenario.ValidUntil;
        var before = await EvidenceAsync(actor.Shop);
        var refused = await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, lease), scenario.Till);
        Assert.True(refused.IsError, "A permit was issued at its own deadline.");
        Assert.Equal(before, await EvidenceAsync(actor.Shop));
        _output.WriteLine("{0}: window [{1:O}, {2:O}) reason {3}; at the deadline issuance answers {4}", cap, permit.NotBefore,
            permit.ValidUntil, permit.LimitingReason, Code(refused));
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
