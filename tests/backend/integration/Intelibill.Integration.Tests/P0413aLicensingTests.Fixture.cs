using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Net;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit.Abstractions;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Wolverine;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class P0413aLicensingTests(PostgreSqlTestFixture fixture, ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    private readonly TestClock _clock = new(EntitlementSnapshotsV1.Instant(TimeProvider.System));
    private string Migrator => fixture.MigratorConnectionString;
    private static CancellationToken Ct => CancellationToken.None;

    private ApiWebApplicationFactory CreateHost() => new(fixture, services =>
    {
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

    private static async Task<Guid> CreateShopAsync(HttpClient client)
    {
        await RegisterAsync(client);
        using var created = await client.PostAsJsonAsync("/api/shops", new
        {
            name = "P04-13a merchant", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001",
            taxProfile = new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" },
        }, Ct);
        var token = (await ReadSuccessAsync(created)).GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return Guid.Parse(new JwtSecurityToken(token).Claims.Single(x => x.Type == "active_shop_id").Value);
    }

    private static async Task RegisterAsync(HttpClient client, string? email = null)
    {
        using var enroll = await client.EnrollAsync(new { email = email ?? $"p0413a-{Guid.NewGuid():N}@fixture.test",
            password = "Pass123!Aa", firstName = "Test", lastName = "Owner" });
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(enroll)).GetProperty("accessToken").GetString());
    }

    private static async Task AddMemberAsync(ApiWebApplicationFactory host, HttpClient owner, HttpClient member,
        Guid shop, string role)
    {
        var email = $"p0413a-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@fixture.test";
        await RegisterAsync(member, email);
        using var created = await owner.PostAsJsonAsync($"/api/shops/{shop}/invitations", new { email, role }, Ct);
        await ReadSuccessAsync(created);
        var invitation = await IdentityTestMailbox.ReadInvitationAsync(host, email);
        using var accepted = await member.PostAsJsonAsync($"/api/invitations/{invitation.Id}/accept",
            new { token = invitation.Token }, Ct);
        await ReadSuccessAsync(accepted);
        using var scoped = await member.PostAsJsonAsync("/api/shops/switch", new { shopId = shop }, Ct);
        member.DefaultRequestHeaders.Authorization = new("Bearer",
            (await ReadSuccessAsync(scoped)).GetProperty("accessToken").GetString());
    }

    private async Task<Binding> BindAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        var membership = await db.ShopMemberships.SingleAsync(x => x.ShopId == shop && x.Role == Intelibill.Domain.Enums.ShopRole.Owner, Ct);
        var binding = new Binding(shop, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var now = _clock.Now;
        foreach (var kind in new[] { "Granted", "Accepted" })
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO public.shop_affiliation_consent_events (id, shop_id, account_id, request_id,
                    actor_id, membership_id, authority_version, operation_key, kind, recorded_at, term_id,
                    shop_name, owner_name, request_expires_at)
                VALUES ({Guid.NewGuid()}, {shop}, {binding.Account}, {binding.Request},
                    {membership.UserId}, {membership.Id}, {membership.AuthorityVersion}, {Guid.NewGuid()}, {kind}, {now},
                    {binding.Term}, {"P04-13a merchant"}, {"Fixture Owner"}, {now.AddDays(15)})
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
        DateTimeOffset? takesEffectAt = null)
    {
        await using var db = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        var now = _clock.Now;
        var proofTime = verified ?? now;
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.Account, binding.Shop, binding.Request, new(revision, proofTime, restriction, null,
                takesEffectAt is { } at ? new(restriction, at, "Fixture restriction") : null),
            new(revision, proofTime, state, binding.Term, "Paid", proofTime.AddDays(-1), end, end, 2, 3));
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
            Assert.True(status == response.StatusCode,
                $"Expected {(int)status} {code}, received {(int)response.StatusCode}.");
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            var top = (body.TryGetProperty("code", out var stableCode) ? stableCode : body.GetProperty("title")).GetString();
            if (top == "Validation.Failed")
                Assert.Contains(body.GetProperty("errors").EnumerateArray(), error => error.GetProperty("code").GetString() == code);
            else
                Assert.Equal(code, top);
        }
    }

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<TResult> DispatchAsync<TResult>(ApiWebApplicationFactory host, HttpClient client, object message)
    {
        await using var services = host.Services.CreateAsyncScope();
        var accessor = services.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var token = new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(token.Claims, "Integration test")),
            RequestServices = services.ServiceProvider,
        };
        try
        {
            return await services.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync<TResult>(message, Ct);
        }
        finally
        {
            accessor.HttpContext = null;
        }
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
