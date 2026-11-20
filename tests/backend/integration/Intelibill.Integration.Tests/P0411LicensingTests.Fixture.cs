using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
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
/// P04-11: seat-consuming membership transitions (acceptance and reinstatement) are counted
/// against the shop's member allowance inside their own transaction, on real PostgreSQL.
/// Enforcement is switched on per shop by test SQL only; nothing here enrolls a shop by
/// configuration.
/// </summary>
[Collection("Integration Tests")]
public sealed partial class P0411LicensingTests(PostgreSqlTestFixture fixture, ITestOutputHelper output)
{
    private const string Password = "Pass123!Aa";
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
    });

    private static HttpClient CreateClient(ApiWebApplicationFactory host) =>
        host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static async Task<Person> EnrollAsync(HttpClient client, string prefix)
    {
        var email = $"p0411-{prefix}-{Guid.NewGuid():N}@fixture.test";
        using var enroll = await client.EnrollAsync(new { email, password = Password, firstName = "Seat", lastName = prefix });
        var body = await ReadSuccessAsync(enroll);
        return new(body.GetProperty("user").GetProperty("id").GetGuid(), email, body.GetProperty("accessToken").GetString()!);
    }

    private static async Task<(Guid Shop, string Token)> CreateShopAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/shops");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new
        {
            name = "P04-11 merchant", address = "42 MG Road", city = "Bengaluru", state = "Karnataka", pincode = "560001",
            taxProfile = new { stateCode = "29", taxRegime = "Unregistered", eInvoiceStatus = "BelowThreshold", invoiceSeriesCode = "A" },
        });
        using var created = await client.SendAsync(request, Ct);
        var shopToken = (await ReadSuccessAsync(created)).GetProperty("accessToken").GetString()!;
        return (Guid.Parse(new JwtSecurityToken(shopToken).Claims.Single(x => x.Type == "active_shop_id").Value), shopToken);
    }

    /// <summary>Owner, shop, licence binding and <paramref name="active"/> extra Active staff.</summary>
    private async Task<Merchant> MerchantAsync(HttpClient client, int allowance, int active, bool enforce = true,
        DateTimeOffset? end = null, string state = "Active")
    {
        var owner = await EnrollAsync(client, "owner");
        var (shop, token) = await CreateShopAsync(client, owner.Token);
        var merchant = new Merchant(shop, owner with { Token = token }, await BindAsync(shop));
        await ApplyAsync(merchant.Binding, end ?? _clock.Now.AddDays(30), allowance: allowance, state: state);
        if (enforce) await EnforceAsync(shop);
        for (var i = 0; i < active; i++)
            await SeedAsync(shop, (await EnrollAsync(client, $"staff{i}")).UserId, MembershipStatus.Active);
        return merchant;
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
                    {binding.Term}, {"P04-11 merchant"}, {"Fixture Owner"}, {now.AddDays(15)})
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

    private async Task ApplyAsync(Binding binding, DateTimeOffset end, long revision = 1, int? allowance = 3,
        string state = "Active", string restriction = "Unrestricted")
    {
        await using var db = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        var now = _clock.Now;
        var snapshot = new EntitlementSnapshotV1(1, EntitlementSnapshotsV1.PolicyVersion, Guid.NewGuid(),
            binding.Account, binding.Shop, binding.Request, new(revision, now, restriction, null),
            new(revision, now, state, binding.Term, "Paid", now.AddDays(-1), end, end, 2, allowance));
        var body = JsonSerializer.SerializeToUtf8Bytes(snapshot, EntitlementSnapshotsV1.Json);
        var proof = new VerifiedServiceAssertion(Guid.NewGuid(), "test-key", "test-public-key", now, now.AddMinutes(1),
            SignedServiceAssertion.Digest(body), "fixture-verified-assertion");
        var fence = new ShopLicenceFence(db, new LicenceActionPolicy(), _clock);
        var result = await new EntitlementProjectionStore(db, _clock, fence).ApplyAsync(snapshot, body, proof, Ct);
        Assert.False(result.IsError, result.IsError ? result.FirstError.Code : null);
        var acknowledgement = JsonSerializer.Deserialize<EntitlementAcknowledgementV1>(result.Value, EntitlementSnapshotsV1.Json);
        Assert.True(acknowledgement?.Outcome == "Applied", acknowledgement?.Disposition);
    }

    /// <summary>Fixture-only seeding; public onboarding runs through invitations, tested separately.</summary>
    private async Task<long> SeedAsync(Guid shop, Guid user, MembershipStatus status, ShopRole role = ShopRole.Staff)
    {
        await using var db = Open(Migrator);
        var membership = ShopMembership.Create(shop, user, role, isDefault: false);
        if (status != MembershipStatus.Active) membership.ChangeAuthority(role, status);
        db.Add(membership);
        await db.SaveChangesAsync(Ct);
        return membership.AuthorityVersion;
    }

    private async Task<Person> PersonAsync(HttpClient client, Guid shop, MembershipStatus status, ShopRole role = ShopRole.Staff)
    {
        var person = await EnrollAsync(client, status.ToString().ToLowerInvariant());
        await SeedAsync(shop, person.UserId, status, role);
        return person;
    }

    private async Task<List<Guid>> StaffAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        return await db.ShopMemberships.Where(x => x.ShopId == shop && x.Role == ShopRole.Staff &&
            x.Status == MembershipStatus.Active).OrderBy(x => x.UserId).Select(x => x.UserId).ToListAsync(Ct);
    }

    private async Task<Guid> FirstStaffAsync(Guid shop) => (await StaffAsync(shop))[0];

    private static async Task<(Guid Id, string Token)> InviteAsync(ApiWebApplicationFactory host, HttpClient client,
        Merchant merchant, string email, string role = "Staff")
    {
        using var request = Authorized(HttpMethod.Post, $"/api/shops/{merchant.Shop}/invitations", merchant.Owner.Token,
            new { email, role });
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await IdentityTestMailbox.ReadInvitationAsync(host, email);
    }

    private static async Task<HttpResponseMessage> AcceptAsync(HttpClient client, Person person, (Guid Id, string Token) offer)
    {
        using var request = Authorized(HttpMethod.Post, $"/api/invitations/{offer.Id}/accept", person.Token,
            new { token = offer.Token });
        return await client.SendAsync(request, Ct);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Merchant merchant, Guid user,
        string status, long expectedVersion, string role = "Staff", string? token = null)
    {
        using var request = Authorized(HttpMethod.Patch, $"/api/shops/{merchant.Shop}/members/{user}",
            token ?? merchant.Owner.Token, new { role, status, expectedVersion });
        return await client.SendAsync(request, Ct);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token, object body)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<int> CountActiveAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        return await db.ShopMemberships.CountAsync(x => x.ShopId == shop && x.Status == MembershipStatus.Active, Ct);
    }

    private async Task<ShopMembership?> MembershipAsync(Guid shop, Guid user)
    {
        await using var db = Open(Migrator);
        return await db.ShopMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.ShopId == shop && x.UserId == user, Ct);
    }

    private async Task<DateTimeOffset?> AcceptedAtAsync(Guid invitation)
    {
        await using var db = Open(Migrator);
        return await db.Set<ShopInvitation>().Where(x => x.Id == invitation).Select(x => x.AcceptedAt).SingleAsync(Ct);
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(Ct);
            Assert.True(status == response.StatusCode,
                $"Expected {(int)status} {code}, received {(int)response.StatusCode}: {text}");
            var body = JsonSerializer.Deserialize<JsonElement>(text);
            Assert.Equal(code, (body.TryGetProperty("code", out var stable) ? stable : body.GetProperty("title")).GetString());
            return body;
        }
    }

    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static ApplicationDbContext Open(string connection) => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(connection).UseSnakeCaseNamingConvention().Options);

    /// <summary>Holds the shop root row so concurrent admissions queue behind it with their earlier locks held.</summary>
    private async Task<NpgsqlConnection> HoldShopRowAsync(Guid shop)
    {
        var connection = new NpgsqlConnection(Migrator);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand($"BEGIN; SELECT 1 FROM shops WHERE id = '{shop}' FOR UPDATE;", connection);
        await command.ExecuteNonQueryAsync(Ct);
        return connection;
    }

    private static async Task ReleaseShopRowAsync(NpgsqlConnection holder)
    {
        await using (var command = new NpgsqlCommand("COMMIT", holder))
            await command.ExecuteNonQueryAsync(Ct);
        await holder.DisposeAsync();
    }

    /// <summary>Waits until <paramref name="count"/> backends of this database are waiting on a lock.</summary>
    private async Task WaitForLockWaitersAsync(int count)
    {
        await using var connection = new NpgsqlConnection(Migrator);
        await connection.OpenAsync(Ct);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var command = new NpgsqlCommand("""
                SELECT count(*)::int FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock' AND cardinality(pg_blocking_pids(pid)) > 0
                """, connection);
            if ((int)(await command.ExecuteScalarAsync(timeout.Token))! >= count) return;
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed record Person(Guid UserId, string Email, string Token);
    private sealed record Merchant(Guid Shop, Person Owner, Binding Binding);
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
