using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0408dLicensingTests
{
    [Fact]
    public async Task RestrictionDeliveredFirstRefusesWaitingBankWriter()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var (binding, end) = await StartOperatingAsync(client);
        await using var holder = Open(Migrator);
        await using var hold = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlRawAsync("LOCK TABLE public.entitlement_request_uses IN ACCESS EXCLUSIVE MODE", Ct);
        await using var delivery = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        await delivery.Database.OpenConnectionAsync(Ct);

        var restricting = ApplyAsync(binding, end, 2, restriction: "Trading", connection: delivery);
        Task<HttpResponseMessage>? writing = null;
        try
        {
            await AssertWaitingAsync(restricting, Pid(holder), Pid(delivery));
            writing = client.PostAsJsonAsync("/api/bank-accounts", Bank("100000000001"), Ct);
            await AssertWaitingAsync(writing, Pid(delivery));
        }
        finally
        {
            await hold.RollbackAsync(Ct);
            await restricting;
        }

        await AssertProblemAsync(await writing!, HttpStatusCode.Forbidden, "Licence.TradingRestricted");
        await AssertNoBankAccountsAsync(binding.Shop);
    }

    [Fact]
    public async Task FenceContentionAndRevocationCannotSaveSettings()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var (binding, _) = await StartOperatingAsync(client);
        var jwt = new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter);
        var sid = Guid.Parse(jwt.Claims.Single(c => c.Type == "sid").Value);
        await using var holder = Open(RestrictedRuntimeRole.ConnectionStringFor(Migrator));
        await using var hold = await holder.Database.BeginTransactionAsync(Ct);
        await holder.Database.ExecuteSqlAsync($"""
            SELECT public.take_merchant_licence_fence({binding.Account}, {binding.Shop}, false)
            """, Ct);
        var path = $"/api/shops/{binding.Shop}/bank-details";
        await AssertProblemAsync(await client.PutAsJsonAsync(path, Bank("100000000001"), Ct),
            HttpStatusCode.Conflict, "Licence.Busy");

        var writing = client.PutAsJsonAsync(path, Bank("100000000001"), Ct);
        try
        {
            await AssertWaitingAsync(writing, Pid(holder));
            await using var db = Open(Migrator);
            await db.Database.ExecuteSqlAsync($"UPDATE auth_sessions SET revoked_at = {_clock.Now} WHERE id = {sid}", Ct);
        }
        finally
        {
            await hold.RollbackAsync(Ct);
        }

        await AssertProblemAsync(await writing, HttpStatusCode.Unauthorized, "Auth.SessionInvalidated");
        await AssertNoBankAccountsAsync(binding.Shop);
    }

    [Fact]
    public async Task ReleasedAccountReturnsToItsExpiredTerm()
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddMinutes(1);
        await ApplyAsync(binding, end, restriction: "Trading");
        await EnforceAsync(shop);
        _clock.Now = end;

        await ApplyAsync(binding, end, 2, state: "Expired", verified: end.AddMinutes(-1), releasedRevision: 1);
        using var recover = await client.PutAsJsonAsync($"/api/shops/{shop}", Profile("Contact recovered", "42 MG Road"), Ct);
        Assert.Equal("Expired", (await ReadSuccessAsync(recover)).GetProperty("setupAccess").GetProperty("status").GetString());
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/shops", Profile("Another", "Different location"), Ct),
            HttpStatusCode.Forbidden, "Licence.Expired");
    }

    private async Task<(Binding Binding, DateTimeOffset End)> StartOperatingAsync(HttpClient client)
    {
        var shop = await CreateShopAsync(client);
        var binding = await BindAsync(shop);
        var end = _clock.Now.AddDays(1);
        await ApplyAsync(binding, end);
        await EnforceAsync(shop);
        return (binding, end);
    }

    private async Task AssertNoBankAccountsAsync(Guid shop)
    {
        await using var db = Open(Migrator);
        Assert.False(await db.BankAccounts.AnyAsync(x => x.ShopId == shop, Ct));
    }
}
