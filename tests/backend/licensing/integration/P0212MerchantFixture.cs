using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// The P02-12 provider host plus a merchant API whose delegated status reads go to a second licensing host on the
/// same licensing database. Both licensing hosts evaluate with the one fixed lifecycle clock; sessions, delegations
/// and MFA keep their own clocks.
/// </summary>
internal sealed class P0212MerchantFixture(P0212Fixture provider, P0209Fixture.Hosts hosts) : IAsyncDisposable
{
    internal P0212Fixture Provider { get; } = provider;
    internal P0209Fixture.Hosts Hosts { get; } = hosts;

    internal static async Task<P0212MerchantFixture> StartAsync(LicensingPostgreSqlFixture database)
    {
        await database.PrepareMerchantAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(database.Bootstrap(LicensingPostgreSqlFixture.MerchantDatabase),
            "TRUNCATE public.rate_limit_buckets");
        var provider = await P0212Fixture.StartAsync(database);
        var signing = new P0201Fixture(database, provider.Provider.Runtime.Database!);
        signing.Clock.Now = P0202Fixture.WholeSecondNow();
        var licensing = P0202Fixture.LicensingApi(signing, configure: services =>
            services.AddKeyedSingleton<TimeProvider>(P0209Fixture.StatusClockKey, provider.Lifecycle));
        var merchant = signing.MerchantApi(handler: () => licensing.Server.CreateHandler(),
            configure: services => services.AddSingleton<TimeProvider>(signing.Clock));
        return new(provider, new P0209Fixture.Hosts(signing, licensing, merchant, provider.Lifecycle, database));
    }

    public async ValueTask DisposeAsync()
    {
        await Hosts.DisposeAsync();
        await Provider.DisposeAsync();
    }
}
