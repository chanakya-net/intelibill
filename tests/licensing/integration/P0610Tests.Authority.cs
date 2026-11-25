using System.Net;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    [Fact]
    public async Task ReadOnlyRecoveryRequiresRecentBillingMfaAndCannotStartARefund()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(3));
        var recheck = RequestPath(package.Account, request) + "/reconcile";
        using var reserved = await P0212Fixture.PostAsync(finance, recheck, new { });
        Assert.Equal(HttpStatusCode.NotFound, reserved.StatusCode);
        Assert.True(reserved.Headers.CacheControl?.NoStore);
        Assert.Equal(0L, await fixture.CountAsync("refund_sends"));
        var other = await P0209Fixture.CreateAccountAsync(package.Payer.Client, "Second account");
        using var unrelated = await P0212Fixture.PostAsync(finance, RequestPath(other, request) + "/simulator",
            new { outcome = "Failed" });
        Assert.Equal(HttpStatusCode.NotFound, unrelated.StatusCode);
        await PumpRefundsAsync(fixture);
        fixture.Checkout.Trial.Catalogue.Clock.Advance(TimeSpan.FromMinutes(5));
        using var stale = await P0212Fixture.PostAsync(finance, recheck, new { });
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.True(stale.Headers.CacheControl?.NoStore);
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
    }

    [Fact]
    public async Task ScopedRuntimeCannotClaimWorkerOrReadPrivateObservations()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(3));
        var login = fixture.Hosts.Database.Login(fixture.Hosts.Fixture.Owner.Database!, "licensing_runtime");
        await using var connection = new NpgsqlConnection(login.ConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await using var scope = new NpgsqlCommand("SELECT set_config('app.billing_account_id',$1,true)", connection, tx);
        scope.Parameters.AddWithValue(package.Account.ToString("D"));
        await scope.ExecuteNonQueryAsync();
        await using var query = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM public.claim_refund($1,$2)),
                (SELECT count(*) FROM public.refund_finance_observation($3,$4)),
                (SELECT count(*) FROM public.refund_attempts),
                (SELECT count(*) FROM public.refund_outcome($3,$4)),
                (SELECT count(*) FROM public.refund_outcome($2,$4))
            """, connection, tx);
        query.Parameters.AddWithValue(Instant);
        foreach (var id in new[] { Guid.NewGuid(), package.Account, request }) query.Parameters.AddWithValue(id);
        await using var result = await query.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        Assert.Equal(new long[] { 0, 0, 0, 1, 0 }, Enumerable.Range(0, 5).Select(result.GetInt64));
    }
}
