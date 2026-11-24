using System.Buffers.Text;
using System.Net.Http.Json;
using System.Text;
using ErrorOr;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0501LicensingTests
{
    [Fact]
    public async Task Invalid_scope_or_evidence_creates_no_permit()
    {
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var otherClient = CreateClient(host);
        var other = await OpenShopAsync(otherClient, verified, verified.AddDays(30));
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30), licensed: false);
        using var legacy = await client.PostAsJsonAsync("/api/sales/invoice-leases/reserve", new { deviceId = Device, blockSize = 25 }, Ct);
        var legacyLease = (await ReadSuccessAsync(legacy)).GetProperty("leaseId").GetGuid();
        actor = await LicenseAsync(actor, verified, verified.AddDays(30));

        _clock.Now = verified.AddHours(1);
        var (lease, captured) = await ReserveAsync(host, actor);
        var grant = Issued(captured).GrantId;
        var (otherLease, otherCaptured) = await ReserveAsync(host, other);
        var otherGrant = Issued(otherCaptured).GrantId;
        var (secondLease, secondCaptured) = await ReserveAsync(host, actor, "p0501-second-device");
        var secondGrant = Issued(secondCaptured).GrantId;
        var done = await SnapshotAsync(host, actor, grant, lease);
        Assert.Equal(1, done.BatchCount);

        // The reservation endpoint is unchanged and its lease has no grant: nothing can anchor it later.
        await RefusedAsync(actor.Shop, "OfflinePermit.LeaseNotNew", () => AdoptAsync(host, actor, legacyLease));
        var missingGrantId = Guid.NewGuid();
        var missingGrantDone = await SnapshotAsync(host, actor, missingGrantId, legacyLease);
        await RefusedAsync(actor.Shop, "OfflinePermit.GrantNotFound", () => IssueAsync(host, actor, missingGrantId, missingGrantDone));
        await RefusedAsync(actor.Shop, "OfflinePermit.ScopeMismatch", async () => (await ReserveAsync(host, actor, useClaims: false)).Grant);
        await RefusedAsync(actor.Shop, "OfflinePermit.ScopeMismatch",
            async () => (await ReserveAsync(host, actor, claims: actor.Claims with { SessionId = Guid.NewGuid() })).Grant);

        // Wrong caller, device, lease, grant or shop.
        await RefusedAsync(actor.Shop, "OfflinePermit.ScopeMismatch", () => IssueAsync(host, actor, grant, done, useClaims: false));
        await RefusedAsync(actor.Shop, "OfflinePermit.ScopeMismatch", () => IssueAsync(host, actor, grant, done, claims: other.Claims));
        await RefusedAsync(actor.Shop, "OfflinePermit.ScopeMismatch",
            async () => await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, grant, lease, "another-device")));
        await RefusedAsync(actor.Shop, "OfflinePermit.ScopeMismatch",
            async () => await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, grant, secondLease)));
        await RefusedAsync(actor.Shop, "OfflinePermit.ScopeMismatch", () => IssueAsync(host, actor, secondGrant, done));
        await RefusedAsync(actor.Shop, "OfflinePermit.GrantNotFound",
            async () => await IssueAsync(host, actor, otherGrant, await SnapshotAsync(host, actor, otherGrant, otherLease)));

        await AssertLiveAuthorityAsync(host, actor, grant, lease);
    }

    /// <summary>
    /// On an unprojected or restricted shop, live actor authority is checked before exposing commercial
    /// licence outcomes: only a live session sees the commercial result, and neither case writes evidence.
    /// </summary>
    [Theory]
    [InlineData(false, "Licence.VerificationUnavailable")]
    [InlineData(true, "Licence.TradingRestricted")]
    public async Task Revoked_session_never_sees_commercial_denial_on_unprojected_or_restricted_shop(
        bool restricted, string expectedLiveCode)
    {
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30), licensed: restricted);
        _clock.Now = verified.AddHours(1);

        Guid? grant = null;
        OfflineSnapshotCompletion? done = null;
        if (restricted)
        {
            var (lease, captured) = await ReserveAsync(host, actor);
            grant = Issued(captured).GrantId;
            done = await SnapshotAsync(host, actor, grant.Value, lease);
            await ApplyAsync(actor.Binding, verified.AddDays(30), revision: 2, restriction: "Trading", suspensionEffectiveAt: _clock.Now);
        }

        // Valid live actor sees the commercial licence outcome with zero evidence writes.
        await RefusedAsync(actor.Shop, expectedLiveCode, async () => (await ReserveAsync(host, actor, "p0501-live")).Grant);
        if (grant is { } g && done is { } d)
            await RefusedAsync(actor.Shop, expectedLiveCode, () => IssueAsync(host, actor, g, d));

        // Revoking the session hides the commercial outcome behind Auth.SessionInvalidated.
        await using var db = Open(Migrator);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE auth_sessions SET revoked_at = {_clock.Now}, revocation_reason = 'p0501-r006' WHERE id = {actor.Session}", Ct);
        await RefusedAsync(actor.Shop, "Auth.SessionInvalidated", async () => (await ReserveAsync(host, actor, "p0501-revoked")).Grant);
        if (grant is { } g2 && done is { } d2)
            await RefusedAsync(actor.Shop, "Auth.SessionInvalidated", () => IssueAsync(host, actor, g2, d2));
    }

    /// <summary>Membership, session, licence and staleness facts known now always win over the stored grant.</summary>
    private async Task AssertLiveAuthorityAsync(ApiWebApplicationFactory host, Actor actor, Guid grant, Guid lease)
    {
        var done = await SnapshotAsync(host, actor, grant, lease);
        await using var db = Open(Migrator);

        // Changed away and back: same role and scope, different versions.
        await db.Database.ExecuteSqlAsync($"UPDATE shop_memberships SET authority_version = authority_version + 2 WHERE user_id = {actor.User}", Ct);
        await RefusedAsync(actor.Shop, "OfflinePermit.AuthorityChanged", () => IssueAsync(host, actor, grant, done));
        await db.Database.ExecuteSqlAsync($"UPDATE shop_memberships SET authority_version = authority_version - 2 WHERE user_id = {actor.User}", Ct);
        await db.Database.ExecuteSqlAsync($"UPDATE auth_sessions SET scope_version = scope_version + 2 WHERE id = {actor.Session}", Ct);
        await RefusedAsync(actor.Shop, "OfflinePermit.AuthorityChanged", () => IssueAsync(host, actor, grant, done));
        await db.Database.ExecuteSqlAsync($"UPDATE auth_sessions SET scope_version = scope_version - 2 WHERE id = {actor.Session}", Ct);

        // Till sessions switched on: an open drawer for today's business date is required.
        var overdueTill = await OpenTillAsync(host, actor, daysAgo: 1);
        await RefusedAsync(actor.Shop, "OfflinePermit.TillRequired", () => IssueAsync(host, actor, grant, done));
        await RefusedAsync(actor.Shop, "OfflinePermit.TillRequired", () => IssueAsync(host, actor, grant, done, overdueTill));
        await RefusedAsync(actor.Shop, "OfflinePermit.TillRequired", () => IssueAsync(host, actor, grant, done, Guid.NewGuid()));
        await db.Database.ExecuteSqlAsync($"UPDATE shop_till_settings SET enabled = false WHERE shop_id = {actor.Shop}", Ct);

        // Stale authority, then a revoked device, then a trading suspension in effect.
        var start = _clock.Now;
        _clock.Now = At("2026-11-22T12:00:00Z");
        await RefusedAsync(actor.Shop, "Licence.VerificationUnavailable", async () => (await ReserveAsync(host, actor)).Grant);
        await RefusedAsync(actor.Shop, "Licence.VerificationUnavailable",
            async () => await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, grant, lease)));

        _clock.Now = start;
        await db.Database.ExecuteSqlAsync($"UPDATE auth_sessions SET revoked_at = {start}, revocation_reason = 'p0501' WHERE id = {actor.Session}", Ct);
        await RefusedAsync(actor.Shop, "Auth.SessionInvalidated", () => IssueAsync(host, actor, grant, done));
        await RefusedAsync(actor.Shop, "Auth.SessionInvalidated", async () => (await ReserveAsync(host, actor)).Grant);
        await db.Database.ExecuteSqlAsync($"UPDATE auth_sessions SET revoked_at = NULL, revocation_reason = NULL WHERE id = {actor.Session}", Ct);

        await ApplyAsync(actor.Binding, start.AddDays(30), revision: 2, restriction: "Trading", suspensionEffectiveAt: start);
        await RefusedAsync(actor.Shop, "Licence.TradingRestricted", () => IssueAsync(host, actor, grant, done));
        await RefusedAsync(actor.Shop, "Licence.TradingRestricted", async () => (await ReserveAsync(host, actor)).Grant);
    }

    [Fact]
    public async Task Historical_validation_rejects_wrong_scope_and_signature()
    {
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var otherClient = CreateClient(host);
        var other = await OpenShopAsync(otherClient, verified, verified.AddDays(30));
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));
        _clock.Now = verified.AddHours(1);
        var (lease, captured) = await ReserveAsync(host, actor);
        var grant = Issued(captured);
        var permit = Issued(await IssueAsync(host, actor, grant.GrantId, await SnapshotAsync(host, actor, grant.GrantId, lease)));
        var scope = ScopeOf(actor, lease);
        var at = _clock.Now;

        Assert.Equal(permit.PermitId, Issued(await VerifyAsync(host, actor, permit.SignedPermit, scope, at)).PermitId);
        foreach (var wrong in new[]
        {
            scope with { UserId = other.User }, scope with { SessionId = other.Session }, scope with { DeviceId = "p0501-Device" },
            scope with { LeaseId = Guid.NewGuid() },
        })
            Assert.Equal("OfflinePermit.ScopeMismatch", Code(await VerifyAsync(host, actor, permit.SignedPermit, wrong, at)));

        // Another shop never retained this key and cannot see it.
        Assert.Equal("OfflinePermit.UnknownKey",
            Code(await VerifyAsync(host, other, permit.SignedPermit, scope with { ShopId = other.Shop }, at)));

        var parts = permit.SignedPermit.Split('.');
        var forgedPayload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(Base64Url.DecodeFromChars(parts[1])).Replace("2026-11-22T12:00:00.000000Z", "2026-12-22T12:00:00.000000Z")));
        Assert.NotEqual(parts[1], forgedPayload);
        var flipped = (parts[2][0] == 'A' ? 'B' : 'A') + parts[2][1..];
        Assert.Equal("OfflinePermit.InvalidSignature", Code(await VerifyAsync(host, actor, $"{parts[0]}.{forgedPayload}.{parts[2]}", scope, at)));
        Assert.Equal("OfflinePermit.InvalidSignature", Code(await VerifyAsync(host, actor, $"{parts[0]}.{parts[1]}.{flipped}", scope, at)));
        // A signed grant is not a permit, whatever key signed it.
        Assert.Equal("OfflinePermit.Malformed", Code(await VerifyAsync(host, actor, grant.SignedGrant, scope, at)));
        foreach (var malformed in new[] { null, "", "a.b", permit.SignedPermit + ".", permit.SignedPermit + "=", new string('A', 9000) })
            Assert.Equal("OfflinePermit.Malformed", Code(await VerifyAsync(host, actor, malformed, scope, at)));
    }

    [Fact]
    public async Task Repeated_completion_returns_the_stored_permit_until_a_limit_tightens()
    {
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));
        _clock.Now = verified.AddHours(1);
        var (lease, captured) = await ReserveAsync(host, actor);
        var grant = Issued(captured).GrantId;
        var done = await SnapshotAsync(host, actor, grant, lease);
        var permit = Issued(await IssueAsync(host, actor, grant, done));
        var stored = await EvidenceAsync(actor.Shop);

        _clock.Now = verified.AddHours(2);
        Assert.Equal(permit, Issued(await IssueAsync(host, actor, grant, done)));
        var racing = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => IssueAsync(host, actor, grant, done)));
        Assert.All(racing, result => Assert.Equal(permit.SignedPermit, Issued(result).SignedPermit));
        Assert.Equal(stored, await EvidenceAsync(actor.Shop));

        // A stricter head arrives: the overlong stored permit is no longer handed out, and stays on record.
        await ApplyAsync(actor.Binding, verified.AddHours(20), revision: 2);
        await RefusedAsync(actor.Shop, "OfflinePermit.Tightened", () => IssueAsync(host, actor, grant, done));
        var shorter = Issued(await IssueAsync(host, actor, grant, await SnapshotAsync(host, actor, grant, lease)));
        Assert.Equal((verified.AddHours(20), OfflineLicencePermitPolicy.CurrentLicenceEnd), (shorter.ValidUntil, shorter.LimitingReason));
        await using var db = Open(Migrator);
        Assert.Equal(permit.SignedPermit, await db.Set<OfflineLicensePermit>().Where(x => x.Id == permit.PermitId)
            .Select(x => x.SignedPermit).SingleAsync(Ct));
    }

    /// <summary>An existing, already saved lease offered to the capture participant.</summary>
    private static async Task<ErrorOr<OfflineGrantEvidence>> AdoptAsync(ApiWebApplicationFactory host, Actor actor, Guid lease)
    {
        using var ambient = AmbientSessionContext.Enter(actor.User, actor.Shop);
        await using var scope = host.Services.CreateAsyncScope();
        var work = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await work.BeginTransactionAsync(Ct);
        var existing = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().InvoiceLeases.SingleAsync(x => x.Id == lease, Ct);
        var result = await scope.ServiceProvider.GetRequiredService<IOfflineLicencePermitIssuer>()
            .CaptureGrantAsync(actor.Claims with { SessionId = existing.ReservedBySessionId ?? actor.Session }, existing, Ct);
        await work.RollbackTransactionAsync(Ct);
        return result;
    }

    private async Task RefusedAsync<T>(Guid shop, string code, Func<Task<ErrorOr<T>>> attempt)
    {
        var before = await EvidenceAsync(shop);
        Assert.Equal(code, Code(await attempt()));
        Assert.Equal(before, await EvidenceAsync(shop));
    }
}
