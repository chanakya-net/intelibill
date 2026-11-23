using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Billing;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Infrastructure.Options;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Intelibill.Integration.Tests;

/// <summary>
/// P05-02: HTTP reservation and bound offline-snapshot permit delivery on real PostgreSQL under
/// the restricted runtime role and a fixed clock. Entitlement projections use a synthetic verified
/// assertion applied through the production projection store (HTTP entitlement signature verification
/// is covered by P0402LicensingTests); grants and permits are signed with locally generated RSA keys.
/// No live licensing provider or Azure Key Vault is contacted.
/// </summary>
public abstract class OfflinePermitHttpFixture(PostgreSqlTestFixture fixture, ITestOutputHelper output) : IDisposable
{
    private protected const string Device = "p0502-device";
    private protected const string OwnerPassword = "Pass123!Aa";
    private protected readonly ITestOutputHelper _output = output;
    private protected readonly TestClock _clock = new(EntitlementSnapshotsV1.Instant(TimeProvider.System));
    private protected readonly PermitKey _keyA = new("p0502-key-a", RSA.Create(2048));
    private protected string Migrator => fixture.MigratorConnectionString;
    private protected static CancellationToken Ct => CancellationToken.None;

    public void Dispose() { _keyA.Rsa.Dispose(); GC.SuppressFinalize(this); }

    private protected static DateTimeOffset At(string instant) =>
        DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture);

    private protected ApiWebApplicationFactory CreateHost(
        PermitKey? key = null, bool configureSigner = true, Action<IServiceCollection>? configure = null) =>
        new(fixture, services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(_clock);
            foreach (var worker in services.Where(x => x.ServiceType == typeof(IHostedService) &&
                (x.ImplementationType == typeof(LeasedHttpDeliveryWorker) ||
                 x.ImplementationType == typeof(ShopAffiliationDeliveryWorker) ||
                 x.ImplementationType == typeof(TrialActivationDeliveryWorker))).ToList())
                services.Remove(worker);

            var request = services.Last(x => x.ServiceType == typeof(ICurrentSessionContext));
            services.RemoveAll<ICurrentSessionContext>();
            services.AddScoped<ICurrentSessionContext>(provider => new AmbientSessionContext(
                (ICurrentSessionContext)(request.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, request.ImplementationType!))));
            services.AddSingleton<IOptionsMonitor<MerchantBillingDelegationOptions>>(
                new PermitKeyOptions(configureSigner ? key ?? _keyA : null));
            configure?.Invoke(services);
        }, runtimeIdentityOutsideRequests: true);

    private protected static HttpClient CreateClient(ApiWebApplicationFactory host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private protected async Task<Actor> OpenShopAsync(
        HttpClient client, DateTimeOffset verified, DateTimeOffset end, bool goods = true,
        Guid? account = null, bool licensed = true)
    {
        _clock.Now = EntitlementSnapshotsV1.Instant(TimeProvider.System);
        var email = $"p0502-{Guid.NewGuid():N}@fixture.test";
        using var enroll = await client.EnrollAsync(new { email, password = OwnerPassword, firstName = "Test", lastName = "Owner" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(enroll)).GetProperty("accessToken").GetString());
        using var created = await client.PostAsJsonAsync("/api/shops", new
        {
            name = "P05-02 merchant", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001",
            taxProfile = new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" },
        }, Ct);
        var token = (await ReadSuccessAsync(created)).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var jwt = new JwtSecurityToken(token);
        var shop = Guid.Parse(jwt.Claims.Single(x => x.Type == "active_shop_id").Value);
        var user = Guid.Parse(jwt.Claims.Single(x => x.Type == "sub").Value);
        var session = Guid.Parse(jwt.Claims.Single(x => x.Type == "sid").Value);
        if (goods) await SeedGoodsAsync(client);

        await PinAuthEndAsync(session, verified.AddDays(400));
        _clock.Now = verified;
        var actor = new Actor(user, shop, session, email, new Binding(shop, Guid.Empty, Guid.Empty, Guid.Empty));
        return licensed ? await LicenseAsync(actor, verified, end, account) : actor;
    }

    private protected async Task<Actor> LicenseAsync(Actor actor, DateTimeOffset verified, DateTimeOffset end, Guid? account = null)
    {
        _clock.Now = verified;
        var binding = await BindAsync(actor.Shop, account);
        await ApplyAsync(binding, end);
        return actor with { Binding = binding };
    }

    private protected async Task<HttpClient> CreateSecondSessionClientAsync(ApiWebApplicationFactory host, Actor actor, DateTimeOffset authUntil)
    {
        var saved = _clock.Now;
        _clock.Now = EntitlementSnapshotsV1.Instant(TimeProvider.System);
        try
        {
            var client = CreateClient(host);
            using var login = await client.PostAsJsonAsync("/api/auth/login/email", new { email = actor.Email, password = OwnerPassword }, Ct);
            await SwitchClientShopAsync(client, (await ReadSuccessAsync(login)).GetProperty("accessToken").GetString()!, actor.Shop, authUntil);
            return client;
        }
        finally { _clock.Now = saved; }
    }

    private protected async Task<HttpClient> CreateStaffClientAsync(ApiWebApplicationFactory host, Guid shopId, DateTimeOffset authUntil)
    {
        var saved = _clock.Now;
        _clock.Now = EntitlementSnapshotsV1.Instant(TimeProvider.System);
        try
        {
            var client = CreateClient(host);
            using var enroll = await client.EnrollAsync(new
            {
                email = $"p0502-staff-{Guid.NewGuid():N}@fixture.test", password = OwnerPassword, firstName = "Shop", lastName = "Staff",
            });
            var enrollToken = (await ReadSuccessAsync(enroll)).GetProperty("accessToken").GetString()!;
            var userId = Guid.Parse(new JwtSecurityToken(enrollToken).Claims.Single(x => x.Type == "sub").Value);
            await using (var db = Open(Migrator))
            {
                db.ShopMemberships.Add(ShopMembership.Create(shopId, userId, ShopRole.Staff, false));
                await db.SaveChangesAsync(Ct);
            }
            await SwitchClientShopAsync(client, enrollToken, shopId, authUntil);
            return client;
        }
        finally { _clock.Now = saved; }
    }

    private protected async Task SwitchClientShopAsync(HttpClient client, string initialToken, Guid shopId, DateTimeOffset authUntil)
    {
        client.DefaultRequestHeaders.Authorization = new("Bearer", initialToken);
        using var switched = await client.PostAsJsonAsync("/api/shops/switch", new { shopId }, Ct);
        var token = (await ReadSuccessAsync(switched)).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var session = Guid.Parse(new JwtSecurityToken(token).Claims.Single(x => x.Type == "sid").Value);
        await PinAuthEndAsync(session, authUntil);
    }

    private protected static async Task SeedGoodsAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = "Fixture stock", barcode = Guid.NewGuid().ToString("N"), uom = "pcs", batchNumber = "LOT",
            quantity = 10m, totalPurchaseCost = 10m, mrp = 2m, salesPrice = 2m, taxRatePercent = 0m,
            taxIncluded = false, purchaseTaxIncluded = false,
        }, Ct);
        await ReadSuccessAsync(response);
    }

    private protected async Task PinAuthEndAsync(Guid session, DateTimeOffset sessionEnd, DateTimeOffset? refreshEnd = null)
    {
        await using var db = Open(Migrator);
        await db.Database.ExecuteSqlAsync($"UPDATE auth_sessions SET expires_at = {sessionEnd} WHERE id = {session}", Ct);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE refresh_tokens SET expires_at = {refreshEnd ?? sessionEnd} WHERE auth_session_id = {session}", Ct);
    }

    private protected async Task<Binding> BindAsync(Guid shop, Guid? account = null)
    {
        await using var db = Open(Migrator);
        var membership = await db.ShopMemberships.SingleAsync(x => x.ShopId == shop && x.Role == ShopRole.Owner, Ct);
        var binding = new Binding(shop, account ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var now = _clock.Now;
        foreach (var kind in new[] { "Granted", "Accepted" })
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id,
                    actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id,
                    shop_name, owner_name, request_expires_at)
                VALUES ({Guid.NewGuid()}, {shop}, {binding.Account}, {binding.Request},
                    {membership.UserId}, {membership.Id}, {membership.AuthorityVersion}, {Guid.NewGuid()}, {kind}, {now},
                    {binding.Term}, {"P05-02 merchant"}, {"Fixture Owner"}, {now.AddDays(15)})
                """, Ct);
        return binding;
    }

    private protected async Task ApplyAsync(
        Binding binding, DateTimeOffset end, long revision = 1, string restriction = "Unrestricted",
        string state = "Active", DateTimeOffset? boundary = null, DateTimeOffset? suspensionEffectiveAt = null,
        long? accountRevision = null)
    {
        await using var db = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        var now = _clock.Now;
        var suspension = suspensionEffectiveAt is { } effectiveAt
            ? new CustomerSuspensionV1(restriction, effectiveAt, "Fixture suspension")
            : null;
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.Account, binding.Shop, binding.Request, new(accountRevision ?? revision, now, restriction, null, suspension),
            new(revision, now, state, binding.Term, "Paid", now.AddDays(-1), end, boundary ?? end, 2, 3));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(1),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var fence = new ShopLicenceFence(db, new LicenceActionPolicy(), _clock);
        var result = await new EntitlementProjectionStore(db, _clock, fence).ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        var ack = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(result.Value, EntitlementSnapshotsV1.Json);
        Assert.True(ack?.Outcome == "Applied", ack?.Disposition);
    }

    private protected async Task EnforceAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync(
            $"UPDATE public.shop_license_states SET enforcement_activated_at = {_clock.Now} WHERE shop_id = {shop}", Ct));
    }

    private protected async Task<Guid> OpenTillAsync(ApiWebApplicationFactory host, Actor actor)
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
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime);
        var till = Till.Create(actor.Shop, $"Front {Guid.NewGuid():N}"[..20], actor.User, now).Value;
        var session = TillSession.Open(actor.Shop, till.Id, actor.User, till.Name, "Fixture Owner", actor.User, now, date, zone, 0m, null).Value;
        db.AddRange(till, session);
        await db.SaveChangesAsync(Ct);
        return session.Id;
    }

    private protected static async Task<JsonElement> ReserveHttpAsync(HttpClient client, string device = Device, int? blockSize = 25)
    {
        using var response = await client.PostAsJsonAsync("/api/sales/invoice-leases/reserve", new { deviceId = device, blockSize }, Ct);
        return await ReadSuccessAsync(response);
    }

    private protected static string BoundStreamUrl(Guid leaseId, Guid grantId, string deviceId, Guid? tillSessionId = null) =>
        $"/api/sales/offline-snapshot/stream?leaseId={leaseId:D}&grantId={grantId:D}&deviceId={Uri.EscapeDataString(deviceId)}" +
        (tillSessionId is { } till ? $"&tillSessionId={till:D}" : string.Empty);

    private protected static async Task<SnapshotStreamResponse> StreamSnapshotHttpAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var records = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToArray();
        return new SnapshotStreamResponse(records);
    }

    private protected static async Task<ErrorOr<OfflineLicensePermitV1>> VerifyAsync(
        ApiWebApplicationFactory host, Actor actor, string? permit, OfflinePermitScope scope, DateTimeOffset at)
    {
        using var ambient = AmbientSessionContext.Enter(actor.User, actor.Shop);
        await using var services = host.Services.CreateAsyncScope();
        return await services.ServiceProvider.GetRequiredService<IOfflineLicencePermitIssuer>().VerifyAsync(permit, scope, at, Ct);
    }

    private protected static OfflinePermitScope ScopeOf(Actor actor, Guid lease, string device = Device) =>
        new(actor.Shop, actor.User, actor.Session, device, lease);

    private protected async Task<Evidence> EvidenceAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        return new(
            await db.Set<OfflineLicenseGrant>().CountAsync(x => x.ShopId == shop, Ct),
            await db.Set<OfflineLicensePermit>().CountAsync(x => x.ShopId == shop, Ct),
            await db.Set<OfflineLicenseVerificationKey>().CountAsync(x => x.ShopId == shop, Ct));
    }

    private protected static string Code<T>(ErrorOr<T> result) => result.IsError ? result.FirstError.Code : "(issued)";

    private protected static T Issued<T>(ErrorOr<T> result)
    {
        Assert.False(result.IsError, result.IsError ? $"{result.FirstError.Code}: {result.FirstError.Description}" : null);
        return result.Value;
    }

    private protected static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(code, (body.TryGetProperty("code", out var stable) ? stable : body.GetProperty("title")).GetString());
        }
    }

    private protected static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private protected static JsonElement RequireLicenceEvidence(JsonElement lease)
    {
        Assert.True(lease.TryGetProperty("licenceEvidence", out var evidence) && evidence.ValueKind == JsonValueKind.Object,
            $"Reservation response did not include licenceEvidence: {lease}");
        return evidence;
    }

    private protected static ApplicationDbContext Open(string connection) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);

    private protected sealed record Actor(Guid User, Guid Shop, Guid Session, string Email, Binding Binding);
    private protected sealed record Binding(Guid Shop, Guid Account, Guid Request, Guid Term);
    private protected sealed record Evidence(int Grants, int Permits, int Keys);

    private protected sealed record SnapshotStreamResponse(JsonElement[] Records)
    {
        public JsonElement Metadata => Records.Single(x => x.GetProperty("type").GetString() == "metadata").GetProperty("metadata");
        public JsonElement Complete => Records.Single(x => x.GetProperty("type").GetString() == "complete").GetProperty("complete");
        public JsonElement[] PermitRecords => Records.Where(x => x.GetProperty("type").GetString() == "licencePermit").ToArray();

        public JsonElement RequirePermitImmediatelyBeforeComplete()
        {
            Assert.True(Records.Length >= 3, $"Expected licencePermit before complete, got {Records.Length} records.");
            Assert.Equal("complete", Records[^1].GetProperty("type").GetString());
            Assert.Equal("licencePermit", Records[^2].GetProperty("type").GetString());
            Assert.Single(PermitRecords);
            return Records[^2].GetProperty("licencePermit");
        }
    }

    private protected sealed record PermitKey(string Id, RSA Rsa)
    {
        public byte[] PublicKey => Rsa.ExportSubjectPublicKeyInfo();
        public string Fingerprint => Convert.ToHexStringLower(SHA256.HashData(PublicKey));
    }

    private protected sealed class PermitKeyOptions(PermitKey? key) : IOptionsMonitor<MerchantBillingDelegationOptions>
    {
        private readonly MerchantBillingDelegationOptions _permit = key is null ? new() : new()
        {
            LocalKeyId = key.Id, LocalPrivateKeyPem = key.Rsa.ExportPkcs8PrivateKeyPem(),
        };
        public MerchantBillingDelegationOptions CurrentValue => Get(null);
        public MerchantBillingDelegationOptions Get(string? name) => name == OfflinePermitContractV1.Section ? _permit : new();
        public IDisposable? OnChange(Action<MerchantBillingDelegationOptions, string?> listener) => null;
    }

    private protected sealed class AmbientSessionContext(ICurrentSessionContext request) : ICurrentSessionContext
    {
        private static readonly AsyncLocal<(Guid User, Guid Shop)?> Caller = new();
        public Guid? UserId => Caller.Value?.User ?? request.UserId;
        public Guid? ActiveShopId => Caller.Value?.Shop ?? request.ActiveShopId;
        public static IDisposable Enter(Guid user, Guid shop) { Caller.Value = (user, shop); return new Leave(); }
        private sealed class Leave : IDisposable { public void Dispose() => Caller.Value = null; }
    }

    private protected sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks = now.UtcTicks;
        public DateTimeOffset Now { get => new(Interlocked.Read(ref _ticks), TimeSpan.Zero); set => Interlocked.Exchange(ref _ticks, value.UtcTicks); }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
