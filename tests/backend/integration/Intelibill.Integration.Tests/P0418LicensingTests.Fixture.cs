using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Application.Common.Licensing;
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
using Npgsql;
using Xunit.Abstractions;

namespace Intelibill.Integration.Tests;

/// <summary>
/// P04-18 source probes: an enrolled merchant shop (schema-owner enrollment, the only local allowlist),
/// a fixture-signed projection applied through the real projection store and a fixed clock.
/// </summary>
[Collection("Integration Tests")]
public sealed partial class P0418LicensingTests(PostgreSqlTestFixture fixture, ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    private readonly TestClock _clock = new(EntitlementSnapshotsV1.Instant(TimeProvider.System));
    private string Migrator => fixture.MigratorConnectionString;
    private static CancellationToken Ct => CancellationToken.None;

    private ApiWebApplicationFactory CreateHost(bool runtimeIdentityOutsideRequests = true, Action<IServiceCollection>? configure = null) =>
        new(fixture, services =>
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(_clock);
        foreach (var worker in services.Where(x => x.ServiceType == typeof(IHostedService) &&
            (x.ImplementationType == typeof(LeasedHttpDeliveryWorker) ||
             x.ImplementationType == typeof(ShopAffiliationDeliveryWorker) ||
             x.ImplementationType == typeof(TrialActivationDeliveryWorker))).ToList())
            services.Remove(worker);
        configure?.Invoke(services);
    }, runtimeIdentityOutsideRequests: runtimeIdentityOutsideRequests);

    private static HttpClient CreateClient(ApiWebApplicationFactory host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    /// <summary>Creates an owner and shop, binds it, applies an operating grant ending at <paramref name="end"/> and enrolls it.</summary>
    private async Task<Binding> OpenEnrolledShopAsync(HttpClient client, DateTimeOffset end)
    {
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        return binding;
    }

    private static async Task<Guid> CreateShopAsync(HttpClient client)
    {
        using var enroll = await client.EnrollAsync(new { email = $"p0418-{Guid.NewGuid():N}@fixture.test",
            password = "Pass123!Aa", firstName = "Test", lastName = "Owner" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(enroll)).GetProperty("accessToken").GetString());
        using var created = await client.PostAsJsonAsync("/api/shops", new
        {
            name = "P04-18 merchant", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001",
            taxProfile = new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" },
        }, Ct);
        var token = (await ReadSuccessAsync(created)).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return Guid.Parse(new JwtSecurityToken(token).Claims.Single(x => x.Type == "active_shop_id").Value);
    }

    private async Task<Binding> BindAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        var membership = await db.ShopMemberships.SingleAsync(x => x.ShopId == shop, Ct);
        var binding = new Binding(shop, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var now = _clock.Now;
        foreach (var kind in new[] { "Granted", "Accepted" })
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id,
                    actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id,
                    shop_name, owner_name, request_expires_at)
                VALUES ({Guid.NewGuid()}, {shop}, {binding.Account}, {binding.Request},
                    {membership.UserId}, {membership.Id}, {membership.AuthorityVersion}, {Guid.NewGuid()}, {kind}, {now},
                    {binding.Term}, {"P04-18 merchant"}, {"Fixture Owner"}, {now.AddDays(15)})
                """, Ct);
        return binding;
    }

    /// <summary>Test-only schema-owner enrollment; no application path, script or configuration sets this column.</summary>
    private async Task EnforceAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"""
            UPDATE public.shop_license_states SET enforcement_activated_at = {_clock.Now}
            WHERE shop_id = {shop}
            """, Ct));
    }

    private async Task ApplyAsync(Binding binding, DateTimeOffset end, long revision = 1,
        string restriction = "Unrestricted", string state = "Active", DateTimeOffset? suspensionEffectiveAt = null)
    {
        await using var db = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        var now = _clock.Now;
        var suspension = suspensionEffectiveAt is { } effectiveAt
            ? new CustomerSuspensionV1(restriction, effectiveAt, "Fixture suspension")
            : null;
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.Account, binding.Shop, binding.Request, new(revision, now, restriction, null, suspension),
            new(revision, now, state, binding.Term, "Paid", now.AddDays(-1), end, end, 2, 3));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(1),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var fence = new ShopLicenceFence(db, new LicenceActionPolicy(), _clock);
        var result = await new EntitlementProjectionStore(db, _clock, fence).ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
    }

    /// <summary>Holds the shop licence fence exclusively, as a projection delivery does, until disposed.</summary>
    private async Task<ApplicationDbContext> HoldFenceAsync(Binding binding)
    {
        var holder = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        await holder.Database.OpenConnectionAsync(Ct);
        await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlAsync(
            $"SELECT public.take_merchant_licence_fence({binding.Account}::uuid, {binding.Shop}, false)", Ct);
        return holder;
    }

    private static int Pid(ApplicationDbContext db) => ((NpgsqlConnection)db.Database.GetDbConnection()).ProcessID;

    /// <summary>True when a backend waits on <paramref name="blocker"/> before <paramref name="request"/> finishes.</summary>
    private async Task<bool> WaitsBehindAsync(Task request, int blocker)
    {
        await using var observer = Open(Migrator);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !request.IsCompleted)
        {
            var waiting = await observer.Database.SqlQuery<int>($"""
                SELECT pid AS "Value" FROM pg_stat_activity
                WHERE wait_event_type = 'Lock' AND {blocker} = ANY(pg_blocking_pids(pid))
                LIMIT 1
                """).SingleOrDefaultAsync(Ct);
            if (waiting != 0)
            {
                _output.WriteLine("PostgreSQL pg_blocking_pids: backend {0} waits for fence holder {1}", waiting, blocker);
                return true;
            }
            await Task.Delay(10, Ct);
        }
        return false;
    }

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        var text = await response.Content.ReadAsStringAsync(Ct);
        return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<Observed> ObserveAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(Ct);
            string? code = null;
            if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith('{'))
            {
                using var body = JsonDocument.Parse(text);
                if (body.RootElement.TryGetProperty("code", out var value)) code = value.GetString();
                else if (!response.IsSuccessStatusCode && body.RootElement.TryGetProperty("title", out var title))
                    code = title.GetString();
            }
            return new(response.StatusCode, code);
        }
    }

    private static ApplicationDbContext Open(string connection) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);

    private sealed record Binding(Guid Shop, Guid Account, Guid Request, Guid Term);

    private sealed record Observed(HttpStatusCode Status, string? Code)
    {
        public override string ToString() => $"{(int)Status} {Code ?? "(no code)"}";
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
