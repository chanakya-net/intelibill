using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Shops.Queries.GetShopDetails;
using Intelibill.Application.Features.Shops.Setup;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408dLicensingTests
{
    private const string Password = "Pass123!Aa";

    [Fact]
    public async Task ExpiryUpdateHonoursAnAuthorityChangeThatCommitsFirst()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddDays(1));
        await EnforceAsync(shop);
        var session = ReadSession(client);
        var before = await ExpiryFactsAsync(shop);

        await using var authority = Open(Migrator);
        await using var hold = await authority.Database.BeginTransactionAsync(Ct);
        await authority.Database.ExecuteSqlAsync($"SELECT 1 FROM users WHERE id = {session.UserId} FOR UPDATE", Ct);
        var row = await authority.AuthSessions.SingleAsync(x => x.Id == session.SessionId, Ct);
        row.Revoke("p0408d-authority-first", _clock.Now);
        await authority.SaveChangesAsync(Ct);

        var writing = client.PutAsJsonAsync("/api/expiry-policies", ExpiryUpdate(before.Version), Ct);
        try
        {
            await AssertWaitingAsync(writing, Pid(authority));
        }
        finally
        {
            await hold.CommitAsync(Ct);
        }

        await AssertProblemAsync(await writing, HttpStatusCode.Forbidden, "Expiry.OwnerRequired");
        Assert.Equal(before, await ExpiryFactsAsync(shop));
    }

    [Fact]
    public async Task AuthorityChangeSerializesBehindAnExpiryUpdateThatWins()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        using var second = CreateClient(host);
        var email = $"p0408d-expiry-{Guid.NewGuid():N}@fixture.test";
        await RegisterAsync(client, email);
        var shop = await CreateShopAsync(client, register: false);
        await LoginAsync(second, email);
        await ApplyAsync(await BindAsync(shop), _clock.Now.AddDays(1));
        await EnforceAsync(shop);
        var session = ReadSession(client);
        var before = await ExpiryFactsAsync(shop);

        await using var barrier = await TillPostgresBarrier.CreateAsync(Migrator, "expiry_policy_revisions", shop);
        var writing = client.PutAsJsonAsync("/api/expiry-policies", ExpiryUpdate(before.Version), Ct);
        var writer = await barrier.WaitForArrivalAsync();
        var revoking = second.DeleteAsync($"/api/users/me/sessions/{session.SessionId}", Ct);
        try
        {
            await AssertWaitingAsync(revoking, writer);
        }
        finally
        {
            await barrier.ReleaseAsync();
            await Task.WhenAll(writing, revoking);
        }

        using var written = await writing;
        // The update's transaction wins. Revocation may still commit before its separate response handoff.
        if (written.StatusCode == HttpStatusCode.ServiceUnavailable)
            await AssertProblemAsync(written, HttpStatusCode.ServiceUnavailable, "Licence.DeliveryUnconfirmed");
        else
            await ReadSuccessAsync(written);
        using var revoked = await revoking;
        Assert.True(revoked.IsSuccessStatusCode, $"Revocation answered {(int)revoked.StatusCode}.");
        Assert.Equal(new ExpiryFacts(before.Version + 1, before.Revisions + 1), await ExpiryFactsAsync(shop));
        await using var db = Open(Migrator);
        Assert.NotNull(await db.AuthSessions.Where(x => x.Id == session.SessionId).Select(x => x.RevokedAt).SingleAsync(Ct));
    }

    [Fact]
    public async Task ShopDetailsRequireTheLiveCallingSession()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var device = CreateClient(host);
        using var other = CreateClient(host);
        using var stranger = CreateClient(host);
        var email = $"p0408d-read-{Guid.NewGuid():N}@fixture.test";
        await RegisterAsync(device, email);
        var assigned = await CreateShopAsync(device, register: false);
        var active = await CreateShopAsync(device, register: false);
        await LoginAsync(other, email);
        await RegisterAsync(stranger);
        var session = ReadSession(device);
        Assert.Equal(active, session.ActiveShopId);

        var live = await ReadDetailsAsync(host, device, new(session.UserId, assigned) { Session = session });
        Assert.False(live.IsError, live.IsError ? live.FirstError.Code : null);
        Assert.Equal(assigned, live.Value.ShopId);

        var foreign = await ReadDetailsAsync(host, device, new(session.UserId, assigned) { Session = ReadSession(stranger) });
        var absent = await ReadDetailsAsync(host, device, new(session.UserId, assigned));
        Assert.Equal("Auth.SessionInvalidated", foreign.FirstError.Code);
        Assert.Equal("Auth.SessionInvalidated", absent.FirstError.Code);

        using var revoke = await other.DeleteAsync($"/api/users/me/sessions/{session.SessionId}", Ct);
        Assert.True(revoke.IsSuccessStatusCode, $"Revocation answered {(int)revoke.StatusCode}.");
        var revoked = await ReadDetailsAsync(host, device, new(session.UserId, assigned) { Session = session });
        var status = await DirectAsync(host, device, services =>
            ActivatorUtilities.CreateInstance<GetShopSetupQueryHandler>(services).HandleAsync(
                new(active, session.UserId, active) { Session = session }, Ct));
        Assert.Equal("Auth.SessionInvalidated", revoked.FirstError.Code);
        Assert.Equal("Auth.SessionInvalidated", status.FirstError.Code);

        await using var db = Open(Migrator);
        Assert.True(await db.ShopMemberships.AnyAsync(x => x.ShopId == assigned && x.UserId == session.UserId
            && x.Status == MembershipStatus.Active, Ct));
    }

    private static Task<ErrorOr<Intelibill.Application.Features.Shops.DTOs.ShopDetailsDto>> ReadDetailsAsync(
        ApiWebApplicationFactory host, HttpClient caller, GetShopDetailsQuery query) =>
        DirectAsync(host, caller, services =>
            ActivatorUtilities.CreateInstance<GetShopDetailsQueryHandler>(services).HandleAsync(query, Ct));

    private async Task<ExpiryFacts> ExpiryFactsAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        var version = await db.Database.SqlQuery<long>(
            $"SELECT catalog_version AS \"Value\" FROM shop_expiry_states WHERE shop_id = {shop}").SingleAsync(Ct);
        return new(version, await db.ExpiryPolicyRevisions.CountAsync(x => x.ShopId == shop, Ct));
    }

    private static object ExpiryUpdate(long version) => new
    {
        expectedCatalogVersion = version,
        policy = new { mode = "Block", expiryRequired = true, sellThroughExpiryDate = true, warningDays = 7, overrideAllowed = false },
        reason = "Authority race",
    };

    private static async Task LoginAsync(HttpClient client, string email)
    {
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { identifier = email, password = Password }, Ct);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await ReadSuccessAsync(login)).GetProperty("accessToken").GetString());
    }

    private static async Task<T> DirectAsync<T>(ApiWebApplicationFactory host, HttpClient caller, Func<IServiceProvider, Task<T>> call)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext { User = ReadPrincipal(caller) };
        try
        {
            return await call(scope.ServiceProvider);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static SessionClaims ReadSession(HttpClient client) => SessionClaims.TryRead(ReadPrincipal(client))!;

    private static ClaimsPrincipal ReadPrincipal(HttpClient client)
    {
        var jwt = new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        return new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims.Append(
            new Claim(ClaimTypes.NameIdentifier, jwt.Subject)), "P04-08d fixture"));
    }

    private sealed record ExpiryFacts(long Version, int Revisions);
}
