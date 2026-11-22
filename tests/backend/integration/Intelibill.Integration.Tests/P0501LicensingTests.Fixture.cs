using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Intelibill.Infrastructure.Billing;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
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
/// P05-01: the registered offline-permit issuer on real PostgreSQL, under the restricted runtime role
/// and a fixed clock. Entitlement projections supply a synthetic, already-verified assertion to the
/// real projection store (entitlement-signature verification is covered separately by P0402LicensingTests);
/// grants and permits are signed with locally generated RSA keys. Nothing here contacts a live
/// licensing provider or Azure Key Vault.
/// </summary>
[Collection("Integration Tests")]
public sealed partial class P0501LicensingTests(PostgreSqlTestFixture fixture, ITestOutputHelper output) : IDisposable
{
    private const string Device = "p0501-device";
    private readonly ITestOutputHelper _output = output;
    private readonly TestClock _clock = new(EntitlementSnapshotsV1.Instant(TimeProvider.System));
    private readonly PermitKey _keyA = new("p0501-key-a", RSA.Create(2048));
    private readonly PermitKey _keyB = new("p0501-key-b", RSA.Create(2048));
    private string Migrator => fixture.MigratorConnectionString;
    private static CancellationToken Ct => CancellationToken.None;

    public void Dispose()
    {
        _keyA.Rsa.Dispose();
        _keyB.Rsa.Dispose();
    }

    private static DateTimeOffset At(string instant) => DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Runtime identity everywhere: issuance outside a request still meets row-level security.</summary>
    private ApiWebApplicationFactory CreateHost(PermitKey? key = null, Action<IServiceCollection>? configure = null) =>
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
            services.AddSingleton<IOptionsMonitor<MerchantBillingDelegationOptions>>(new PermitKeyOptions(key ?? _keyA));
            configure?.Invoke(services);
        }, runtimeIdentityOutsideRequests: true);

    private static HttpClient CreateClient(ApiWebApplicationFactory host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    /// <summary>
    /// An owner, shop and operating head verified at <paramref name="verified"/>. Requests run at the real
    /// time their tokens need; the scenario then pins the clock and the session's own end.
    /// </summary>
    private async Task<Actor> OpenShopAsync(HttpClient client, DateTimeOffset verified, DateTimeOffset end, bool goods = true,
        Guid? account = null, bool licensed = true)
    {
        _clock.Now = EntitlementSnapshotsV1.Instant(TimeProvider.System);
        using var enroll = await client.EnrollAsync(new { email = $"p0501-{Guid.NewGuid():N}@fixture.test",
            password = "Pass123!Aa", firstName = "Test", lastName = "Owner" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(enroll)).GetProperty("accessToken").GetString());
        using var created = await client.PostAsJsonAsync("/api/shops", new
        {
            name = "P05-01 merchant", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001",
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
        await using var db = Open(Migrator);
        var row = await db.AuthSessions.AsNoTracking().SingleAsync(x => x.Id == session, Ct);
        var actor = new Actor(user, shop, session, new SessionClaims(user, session, row.SecurityVersion, row.ScopeVersion, shop, "Owner"),
            new Binding(shop, Guid.Empty, Guid.Empty, Guid.Empty));
        return licensed ? await LicenseAsync(actor, verified, end, account) : actor;
    }

    /// <summary>Binds the shop to an account and applies its first operating head, verified at <paramref name="verified"/>.</summary>
    private async Task<Actor> LicenseAsync(Actor actor, DateTimeOffset verified, DateTimeOffset end, Guid? account = null)
    {
        var binding = await BindAsync(actor.Shop, account);
        _clock.Now = verified;
        await ApplyAsync(binding, end);
        return actor with { Binding = binding };
    }

    private static async Task<Guid> SeedGoodsAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName = "Fixture stock", barcode = Guid.NewGuid().ToString("N"), uom = "pcs", batchNumber = "LOT",
            quantity = 10m, totalPurchaseCost = 10m, mrp = 2m, salesPrice = 2m, taxRatePercent = 0m,
            taxIncluded = false, purchaseTaxIncluded = false,
        }, Ct);
        return (await ReadSuccessAsync(response)).GetProperty("inventoryBatchId").GetGuid();
    }

    /// <summary>Fixes when the device session and its refresh credentials end, as the auth deadline reads them.</summary>
    private async Task PinAuthEndAsync(Guid session, DateTimeOffset sessionEnd, DateTimeOffset? refreshEnd = null)
    {
        await using var db = Open(Migrator);
        await db.Database.ExecuteSqlAsync($"UPDATE auth_sessions SET expires_at = {sessionEnd} WHERE id = {session}", Ct);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE refresh_tokens SET expires_at = {refreshEnd ?? sessionEnd} WHERE auth_session_id = {session}", Ct);
    }

    private async Task<Binding> BindAsync(Guid shop, Guid? account = null)
    {
        await using var db = Open(Migrator);
        var membership = await db.ShopMemberships.SingleAsync(x => x.ShopId == shop, Ct);
        var binding = new Binding(shop, account ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var now = _clock.Now;
        foreach (var kind in new[] { "Granted", "Accepted" })
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id,
                    actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id,
                    shop_name, owner_name, request_expires_at)
                VALUES ({Guid.NewGuid()}, {shop}, {binding.Account}, {binding.Request},
                    {membership.UserId}, {membership.Id}, {membership.AuthorityVersion}, {Guid.NewGuid()}, {kind}, {now},
                    {binding.Term}, {"P05-01 merchant"}, {"Fixture Owner"}, {now.AddDays(15)})
                """, Ct);
        return binding;
    }

    /// <summary>
    /// Applies a synthetic, already-verified entitlement assertion through the production projection store.
    /// Entitlement HTTP signature verification is exercised separately by P0402LicensingTests.
    /// </summary>
    private async Task ApplyAsync(Binding binding, DateTimeOffset end, long revision = 1, string restriction = "Unrestricted",
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
        var acknowledgement = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(result.Value, EntitlementSnapshotsV1.Json);
        Assert.True(acknowledgement?.Outcome == "Applied", acknowledgement?.Disposition);
    }

    /// <summary>
    /// What a reservation does, through the same production services, with the issuer taking part
    /// before the lease is saved. By default a refused capture rolls the reservation back; callers
    /// testing legacy fallback may commit the outer unanchored lease instead.
    /// </summary>
    private async Task<(Guid Lease, ErrorOr<OfflineGrantEvidence> Grant)> ReserveAsync(ApiWebApplicationFactory host, Actor actor,
        string device = Device, SessionClaims? claims = null, bool useClaims = true, DateTimeOffset? leaseExpiresAt = null,
        bool commitLegacyOnRefusal = false)
    {
        using var ambient = AmbientSessionContext.Enter(actor.User, actor.Shop);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var shop = (await services.GetRequiredService<IShopRepository>().GetByIdAsync(actor.Shop, Ct))!;
        var profile = InvoiceDocumentBuilder.CaptureSellerProfile(shop).Value;
        var reservedAt = _clock.Now;
        var year = FiscalYear.ForDate(reservedAt);
        var fullTerm = reservedAt.AddDays(InvoiceLeaseDefaults.LeaseDurationDays);
        var work = services.GetRequiredService<IUnitOfWork>();
        await work.BeginTransactionAsync(Ct);
        var range = await services.GetRequiredService<IInvoiceNumberAllocator>().ReserveRangeAsync(
            new InvoiceSeriesKey(shop.Id, shop.SellerRegistrationKey, profile.SeriesCode, year), 25, Ct);
        Assert.False(range.IsError, range.IsError ? range.FirstError.Code : null);
        var lease = InvoiceLease.Create(shop.Id, range.Value.SequenceId, device, year.StartYear, range.Value.Prefix,
            range.Value.Start, range.Value.End, InvoiceNumberFormat.CounterPadding, reservedAt,
            leaseExpiresAt ?? (fullTerm < year.NextStartsAt ? fullTerm : year.NextStartsAt), actor.User, actor.Session, profile);
        await services.GetRequiredService<IInvoiceLeaseRepository>().AddAsync(lease, Ct);
        var grant = await services.GetRequiredService<IOfflineLicencePermitIssuer>()
            .CaptureGrantAsync(useClaims ? claims ?? actor.Claims : null, lease, Ct);
        if (grant.IsError && !commitLegacyOnRefusal)
        {
            await work.RollbackTransactionAsync(Ct);
            return (lease.Id, grant);
        }
        await work.SaveChangesAsync(Ct);
        await work.CommitTransactionAsync(Ct);
        return (lease.Id, grant);
    }

    /// <summary>Starts a server-owned snapshot capture and enumerates its stream to completion.</summary>
    private static async Task<OfflineSnapshotCompletion> SnapshotAsync(ApiWebApplicationFactory host, Actor actor, Guid grant,
        Guid lease, string device = Device, SessionClaims? claims = null)
    {
        using var ambient = AmbientSessionContext.Enter(actor.User, actor.Shop);
        await using var scope = host.Services.CreateAsyncScope();
        var issuer = scope.ServiceProvider.GetRequiredService<IOfflineLicencePermitIssuer>();
        var capture = issuer.StartSnapshotCapture(claims ?? actor.Claims, grant, lease, device);
        await foreach (var _ in capture.StreamAsync(Ct))
        {
        }
        Assert.NotNull(capture.Completion);
        return capture.Completion!;
    }

    private static async Task<ErrorOr<OfflinePermitEvidence>> IssueAsync(ApiWebApplicationFactory host, Actor actor, Guid grant,
        OfflineSnapshotCompletion? completion, Guid? till = null, SessionClaims? claims = null, bool useClaims = true)
    {
        using var ambient = AmbientSessionContext.Enter(actor.User, actor.Shop);
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOfflineLicencePermitIssuer>().IssueAsync(
            new(useClaims ? claims ?? actor.Claims : null, grant, completion, till), Ct);
    }

    private static async Task<ErrorOr<OfflineLicensePermitV1>> VerifyAsync(ApiWebApplicationFactory host, Actor actor, string? permit,
        OfflinePermitScope scope, DateTimeOffset at)
    {
        using var ambient = AmbientSessionContext.Enter(actor.User, actor.Shop);
        await using var services = host.Services.CreateAsyncScope();
        return await services.ServiceProvider.GetRequiredService<IOfflineLicencePermitIssuer>().VerifyAsync(permit, scope, at, Ct);
    }

    private static OfflinePermitScope ScopeOf(Actor actor, Guid lease, string device = Device) =>
        new(actor.Shop, actor.User, actor.Session, device, lease);

    private async Task<Evidence> EvidenceAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        return new(await db.Set<OfflineLicenseGrant>().CountAsync(x => x.ShopId == shop, Ct),
            await db.Set<OfflineLicensePermit>().CountAsync(x => x.ShopId == shop, Ct),
            await db.Set<OfflineLicenseVerificationKey>().CountAsync(x => x.ShopId == shop, Ct));
    }

    private static string Code<T>(ErrorOr<T> result) => result.IsError ? result.FirstError.Code : "(issued)";

    private static T Issued<T>(ErrorOr<T> result)
    {
        Assert.False(result.IsError, result.IsError ? $"{result.FirstError.Code}: {result.FirstError.Description}" : null);
        return result.Value;
    }

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static ApplicationDbContext Open(string connection) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);

    private sealed record Actor(Guid User, Guid Shop, Guid Session, SessionClaims Claims, Binding Binding);
    private sealed record Binding(Guid Shop, Guid Account, Guid Request, Guid Term);
    private sealed record Evidence(int Grants, int Permits, int Keys);

    private sealed record PermitKey(string Id, RSA Rsa)
    {
        public byte[] PublicKey => Rsa.ExportSubjectPublicKeyInfo();
        public string Fingerprint => Convert.ToHexStringLower(SHA256.HashData(PublicKey));
    }

    /// <summary>Only the offline-permit section carries a key; every other signing purpose stays unconfigured.</summary>
    private sealed class PermitKeyOptions(PermitKey? key, bool everyPurpose = false) : IOptionsMonitor<MerchantBillingDelegationOptions>
    {
        private readonly MerchantBillingDelegationOptions _permit = key is null ? new() : new()
        {
            LocalKeyId = key.Id, LocalPrivateKeyPem = key.Rsa.ExportPkcs8PrivateKeyPem(),
        };

        public MerchantBillingDelegationOptions CurrentValue => Get(null);
        public MerchantBillingDelegationOptions Get(string? name) =>
            everyPurpose || name == OfflinePermitContractV1.Section ? _permit : new();
        public IDisposable? OnChange(Action<MerchantBillingDelegationOptions, string?> listener) => null;
    }

    /// <summary>The request's own session context, unless a test stands in as an authenticated caller outside a request.</summary>
    private sealed class AmbientSessionContext(ICurrentSessionContext request) : ICurrentSessionContext
    {
        private static readonly AsyncLocal<(Guid User, Guid Shop)?> Caller = new();

        public Guid? UserId => Caller.Value?.User ?? request.UserId;
        public Guid? ActiveShopId => Caller.Value?.Shop ?? request.ActiveShopId;

        public static IDisposable Enter(Guid user, Guid shop)
        {
            Caller.Value = (user, shop);
            return new Leave();
        }

        private sealed class Leave : IDisposable
        {
            public void Dispose() => Caller.Value = null;
        }
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks = now.UtcTicks;
        public DateTimeOffset Now
        {
            get => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _ticks, value.UtcTicks);
        }
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
