using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0502LicensingTests
{
    [Fact]
    public async Task Fallback_reservations_stay_legacy_and_refusals_roll_back()
    {
        var verified = At("2026-11-20T12:00:00Z");

        // 1) Unlicensed (unprojected, observe-only) shop falls back to a legacy lease with zero evidence.
        using (var host = CreateHost())
        {
            await host.InitializeAsync();
            using var client = CreateClient(host);
            var unlicensed = await OpenShopAsync(client, verified, verified.AddDays(30), licensed: false);
            _clock.Now = verified.AddHours(1);

            var legacy = await ReserveHttpAsync(client);
            AssertNoLicenceEvidence(legacy);
            Assert.Equal(new Evidence(0, 0, 0), await EvidenceAsync(unlicensed.Shop));

            // 2) Session without a usable refresh credential is refused and rolls back its sequence range.
            var licensed = await OpenShopAsync(CreateClient(host), verified, verified.AddDays(30));
            using var expiredRefreshClient = await CreateSecondSessionClientAsync(host, licensed, verified.AddDays(30));
            var secondSessionId = ReadSessionId(expiredRefreshClient);
            _clock.Now = verified.AddHours(1);
            await PinAuthEndAsync(secondSessionId, verified.AddDays(30), _clock.Now);

            var denied = await expiredRefreshClient.PostAsJsonAsync(
                "/api/sales/invoice-leases/reserve", new { deviceId = Device, blockSize = 25 }, Ct);
            await AssertProblemAsync(denied, HttpStatusCode.Unauthorized, "Auth.SessionInvalidated");
            Assert.Equal(new Evidence(0, 0, 0), await EvidenceAsync(licensed.Shop));
            await using (var db = Open(Migrator))
            {
                Assert.False(await db.InvoiceLeases.AnyAsync(x => x.ShopId == licensed.Shop, Ct));
                Assert.False(await db.InvoiceSequences.AnyAsync(x => x.ShopId == licensed.Shop, Ct));
            }

            // 3) Enforced trading restriction refuses the reservation and rolls back everything.
            await ApplyAsync(licensed.Binding, verified.AddDays(30), revision: 2, restriction: "Trading", suspensionEffectiveAt: _clock.Now);
            await EnforceAsync(licensed.Shop);
            using var ownerClient = await CreateSecondSessionClientAsync(host, licensed, verified.AddDays(30));
            var restricted = await ownerClient.PostAsJsonAsync(
                "/api/sales/invoice-leases/reserve", new { deviceId = Device, blockSize = 25 }, Ct);
            await AssertProblemAsync(restricted, HttpStatusCode.Forbidden, "Licence.TradingRestricted");
            await using (var db = Open(Migrator))
                Assert.False(await db.InvoiceLeases.AnyAsync(x => x.ShopId == licensed.Shop, Ct));
        }

        // 4) Licensed shop with no offline-permit signing key falls back to a legacy lease with zero evidence.
        using (var noSignerHost = CreateHost(configureSigner: false))
        {
            await noSignerHost.InitializeAsync();
            using var client = CreateClient(noSignerHost);
            var actor = await OpenShopAsync(client, verified, verified.AddDays(30));
            _clock.Now = verified.AddHours(1);

            var legacy = await ReserveHttpAsync(client);
            AssertNoLicenceEvidence(legacy);
            Assert.Equal(new Evidence(0, 0, 0), await EvidenceAsync(actor.Shop));
        }
    }

    [Fact]
    public async Task Unbound_stream_is_unchanged_and_wrong_scope_writes_nothing()
    {
        var verified = At("2026-11-20T12:00:00Z");
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var otherClient = CreateClient(host);
        var other = await OpenShopAsync(otherClient, verified, verified.AddDays(30));
        var actor = await OpenShopAsync(client, verified, verified.AddDays(30));
        using var secondSessionClient = await CreateSecondSessionClientAsync(host, actor, verified.AddDays(30));
        using var staffClient = await CreateStaffClientAsync(host, actor.Shop, verified.AddDays(30));

        _clock.Now = verified.AddHours(1);
        var lease = await ReserveHttpAsync(client);
        var leaseId = lease.GetProperty("leaseId").GetGuid();
        var grantId = RequireLicenceEvidence(lease).GetProperty("grantId").GetGuid();
        var otherLease = await ReserveHttpAsync(otherClient);
        var otherLeaseId = otherLease.GetProperty("leaseId").GetGuid();
        var otherGrantId = RequireLicenceEvidence(otherLease).GetProperty("grantId").GetGuid();

        // Unbound stream stays byte-compatible (schema 4, zero licencePermit records, zero permits stored).
        var unbound = await StreamSnapshotHttpAsync(client, "/api/sales/offline-snapshot/stream");
        Assert.Equal(4, unbound.Metadata.GetProperty("schemaVersion").GetInt32());
        Assert.Empty(unbound.PermitRecords);
        Assert.Equal("complete", unbound.Records[^1].GetProperty("type").GetString());
        Assert.Equal(new Evidence(1, 0, 1), await EvidenceAsync(actor.Shop));

        // Partial or empty bindings return HTTP 400.
        foreach (var badUrl in new[]
        {
            $"/api/sales/offline-snapshot/stream?leaseId={leaseId:D}",
            $"/api/sales/offline-snapshot/stream?grantId={grantId:D}",
            $"/api/sales/offline-snapshot/stream?deviceId={Device}",
            $"/api/sales/offline-snapshot/stream?leaseId={leaseId:D}&grantId={grantId:D}",
            $"/api/sales/offline-snapshot/stream?leaseId={leaseId:D}&grantId={grantId:D}&deviceId=%20",
            $"/api/sales/offline-snapshot/stream?leaseId={Guid.Empty:D}&grantId={grantId:D}&deviceId={Device}",
        })
        {
            using var bad = await client.GetAsync(badUrl, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        // Wrong shop, device, lease, grant, session and Staff caller receive refused without writing permits.
        var attempts = new (string Case, HttpClient Caller, string Url, string ExpectedCode)[]
        {
            ("wrong-shop-grant", client, BoundStreamUrl(otherLeaseId, otherGrantId, Device), "OfflinePermit.GrantNotFound"),
            ("wrong-grant", client, BoundStreamUrl(leaseId, Guid.NewGuid(), Device), "OfflinePermit.GrantNotFound"),
            ("wrong-device", client, BoundStreamUrl(leaseId, grantId, "another-device"), "OfflinePermit.ScopeMismatch"),
            ("wrong-lease", client, BoundStreamUrl(Guid.NewGuid(), grantId, Device), "OfflinePermit.ScopeMismatch"),
            ("wrong-session", secondSessionClient, BoundStreamUrl(leaseId, grantId, Device), "OfflinePermit.ScopeMismatch"),
            ("staff-caller", staffClient, BoundStreamUrl(leaseId, grantId, Device), "Auth.SessionInvalidated"),
        };

        var before = await EvidenceAsync(actor.Shop);
        foreach (var (name, caller, url, expectedCode) in attempts)
        {
            var stream = await StreamSnapshotHttpAsync(caller, url);
            var permit = stream.RequirePermitImmediatelyBeforeComplete();
            Assert.Equal("refused", permit.GetProperty("status").GetString());
            Assert.Equal(expectedCode, permit.GetProperty("refusalCode").GetString());
            Assert.False(permit.TryGetProperty("description", out _), $"{name}: refused record must not leak description.");
            Assert.Equal(before, await EvidenceAsync(actor.Shop));
        }

        Assert.Equal(new Evidence(1, 0, 1), await EvidenceAsync(other.Shop));
    }

    private static void AssertNoLicenceEvidence(JsonElement lease)
    {
        if (lease.TryGetProperty("licenceEvidence", out var evidence))
            Assert.Equal(JsonValueKind.Null, evidence.ValueKind);
    }

    private static Guid ReadSessionId(HttpClient client)
    {
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter!);
        return Guid.Parse(jwt.Claims.Single(x => x.Type == "sid").Value);
    }
}
