using Microsoft.EntityFrameworkCore;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0213Tests
{
    private async Task AssertRetainedMigrationsAsync(P0213Fixture fixture)
    {
        await using var licensing = fixture.Hosts.Fixture.OpenMigrationContext();
        await using var merchant = MerchantDb(database);
        Assert.Empty(await licensing.Database.GetPendingMigrationsAsync());
        Assert.Empty(await merchant.Database.GetPendingMigrationsAsync());
    }
}
