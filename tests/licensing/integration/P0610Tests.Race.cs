using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0610Tests
{
    [Fact]
    public async Task InFlightProviderCallReleasesAccountLockAndCannotOverwriteNewerLease()
    {
        await using var fixture = await P0609Tests.StartAsync(database, Instant, P0310Tests.Settings());
        using var owner = await fixture.Checkout.Trial.Catalogue.SignInOwnerAsync();
        await P0310Tests.SelectProfileAsync(owner, "a", 0);
        var (package, request, _, _) = await RequestedAsync(fixture, 6000);
        using var finance = await P0306Tests.SignInBillingAsync(fixture.Checkout);
        await ApproveEndAsync(finance, package.Account, request, Instant.AddDays(3));
        await using var remote = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        await remote.OpenAsync();
        await using var barrier = await remote.BeginTransactionAsync();
        await using var hold = new NpgsqlCommand("LOCK TABLE development_gateway_refunds IN ACCESS EXCLUSIVE MODE", remote, barrier);
        await hold.ExecuteNonQueryAsync();
        var first = PumpRefundsAsync(fixture);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            while (!Equals(true, await fixture.ScalarAsync("""
                SELECT EXISTS(SELECT FROM pg_locks WHERE relation='development_gateway_refunds'::regclass
                    AND mode='RowExclusiveLock' AND NOT granted)
                """)))
                await Task.Delay(25, deadline.Token);
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
                SET lock_timeout='200ms'; SELECT pg_advisory_xact_lock(hashtextextended('b{package.Account:D}',0))
                """);
            await P0310Tests.SelectProfileAsync(owner, "b", 1);
            fixture.At(Instant.AddMinutes(2));
            var token = Guid.NewGuid();
            var runtime = fixture.Hosts.Database.Login(fixture.Hosts.Fixture.Owner.Database!, "licensing_runtime");
            Assert.Equal(true, await ScalarAsync(runtime, "SELECT sent FROM claim_refund($1,$2)", Instant.AddMinutes(2), token));
        }
        finally { await barrier.RollbackAsync(); await first; }

        Assert.Equal(0L, await fixture.CountAsync("refund_observations"));
        fixture.At(Instant.AddMinutes(4));
        await PumpRefundsAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("refund_sends"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_refunds", "provider_account_id='simulator-a'"));
        Assert.Equal(0L, await fixture.CountAsync("development_gateway_refunds", "provider_account_id='simulator-b'"));
        Assert.Equal(1L, await fixture.CountAsync("refund_observations", "outcome='Confirmed'"));
    }
}
