using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Infrastructure.Billing;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class P0408aLicensingTests(PostgreSqlTestFixture fixture, ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    private readonly TestClock _clock = new(EntitlementSnapshotsV1.Instant(TimeProvider.System));
    private string Migrator => fixture.MigratorConnectionString;
    private static CancellationToken Ct => CancellationToken.None;

    private ApiWebApplicationFactory CreateHost(IExternalProductLookupService? lookup = null, IProductHubNotifier? notifier = null) => new(fixture, services =>
    {
        if (lookup is not null)
        {
            services.RemoveAll<IExternalProductLookupService>();
            services.AddSingleton(lookup);
        }
        if (notifier is not null)
        {
            services.RemoveAll<IProductHubNotifier>();
            services.AddSingleton(notifier);
        }
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(_clock);
        foreach (var worker in services.Where(x => x.ServiceType == typeof(IHostedService) &&
            (x.ImplementationType == typeof(LeasedHttpDeliveryWorker) ||
             x.ImplementationType == typeof(ShopAffiliationDeliveryWorker) ||
             x.ImplementationType == typeof(TrialActivationDeliveryWorker))).ToList())
            services.Remove(worker);
    }, runtimeIdentityOutsideRequests: true);

    private static HttpClient CreateClient(ApiWebApplicationFactory host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<Guid> CreateShopAsync(HttpClient client, bool hasTaxProfile = true)
    {
        await RegisterAsync(client);
        using var created = await client.PostAsJsonAsync("/api/shops", new
        {
            name = "P04-08a merchant", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001",
            taxProfile = hasTaxProfile ? new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" } : null,
        }, Ct);
        var token = (await ReadSuccessAsync(created)).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return Guid.Parse(new JwtSecurityToken(token).Claims.Single(x => x.Type == "active_shop_id").Value);
    }

    private static async Task RegisterAsync(HttpClient client)
    {
        using var enroll = await client.EnrollAsync(new { email = $"p0408a-{Guid.NewGuid():N}@fixture.test",
            password = "Pass123!Aa", firstName = "Test", lastName = "Owner" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(enroll)).GetProperty("accessToken").GetString());
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
                    {binding.Term}, {"P04-08a merchant"}, {"Fixture Owner"}, {now.AddDays(15)})
                """, Ct);
        return binding;
    }

    private async Task EnforceAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"""
            UPDATE public.shop_license_states SET enforcement_activated_at = {_clock.Now}
            WHERE shop_id = {shop}
            """, Ct));
    }

    private async Task ApplyAsync(Binding binding, DateTimeOffset end, long revision = 1,
        string restriction = "Unrestricted", string state = "Active", DateTimeOffset? verified = null,
        ApplicationDbContext? connection = null, long? releasedRevision = null, string basis = "Paid")
    {
        await using var owned = connection is null ? Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator)) : null;
        var db = connection ?? owned!;
        var now = _clock.Now;
        var proofTime = verified ?? now;
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.Account, binding.Shop, binding.Request, new(revision, proofTime, restriction, releasedRevision),
            new(revision, proofTime, state, binding.Term, basis, proofTime.AddDays(-1), end, end, 2, 3));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(1),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var fence = new ShopLicenceFence(db, new LicenceActionPolicy(), _clock);
        var result = await new EntitlementProjectionStore(db, _clock, fence).ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        var acknowledgement = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(result.Value, EntitlementSnapshotsV1.Json);
        Assert.True(acknowledgement?.Outcome == "Applied", acknowledgement?.Disposition);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal(code, (body.TryGetProperty("code", out var stableCode) ? stableCode : body.GetProperty("title")).GetString());
        }
    }

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static ApplicationDbContext Open(string connection) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);

    private sealed record Binding(Guid Shop, Guid Account, Guid Request, Guid Term);
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
