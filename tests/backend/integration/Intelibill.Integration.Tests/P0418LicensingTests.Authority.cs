using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Integration.Tests;

public sealed partial class P0418LicensingTests
{
    [Theory]
    [InlineData("discount-create")]
    [InlineData("item-reactivate")]
    [InlineData("supplier-delete")]
    [InlineData("batch-void")]
    [InlineData("posting-period-close")]
    public async Task Source_mutation_rechecks_session_after_waiting_on_the_fence(string route)
    {
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var binding = await OpenEnrolledShopAsync(client, _clock.Now.AddMinutes(1));
        var write = route == "item-reactivate"
            ? await PrepareCapacityWriteAsync(client, route)
            : await PrepareCorrectionAsync(client, route);
        var before = await CountSourceFactsAsync(binding.Shop) + await CountCapacityRowsAsync(binding.Shop);
        var session = Guid.Parse(new JwtSecurityToken(client.DefaultRequestHeaders.Authorization!.Parameter!)
            .Claims.Single(x => x.Type == "sid").Value);

        await using var holder = await HoldFenceAsync(binding);
        var pending = write();
        Assert.True(await WaitsBehindAsync(pending, Pid(holder)));
        await using var db = Open(Migrator);
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"""
            UPDATE auth_sessions SET revoked_at = {_clock.Now} WHERE id = {session}
            """, Ct));
        await holder.Database.RollbackTransactionAsync(Ct);

        using var response = await pending;
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        Assert.Equal(before, await CountSourceFactsAsync(binding.Shop) + await CountCapacityRowsAsync(binding.Shop));
    }
}
