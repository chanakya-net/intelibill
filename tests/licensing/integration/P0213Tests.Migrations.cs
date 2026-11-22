using System.Net;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0213Tests
{
    private const string PreviousLicensing = "20261116190000_AddTrialExtensions";
    private const string PreviousMerchant = "20261116160000_AddTrialActivationConsents";

    [Fact]
    public async Task ExplicitMigrationsPreservePopulatedPredecessorsWithTransportDisabled()
    {
        await using var isolated = new IsolatedLicensingPostgreSqlFixture();
        await isolated.InitializeAsync();
        await VerifyPredecessorUpgradeAsync(isolated);
    }

    private static async Task VerifyPredecessorUpgradeAsync(LicensingPostgreSqlFixture isolated)
    {
        await using var fixture = await P0210Fixture.StartAsync(isolated, publishEntitlements: false);
        var owner = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(owner.Client);
        await fixture.LinkAsync(account, owner.UserId, owner);
        await P0209Tests.PostStockAsync(owner.Client, "Predecessor stock");
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(owner, fixture.Accept())).Status);
        var facts = await P0211Tests.OriginalFactsAsync(fixture, owner.ShopId);

        await using var licensing = fixture.Hosts.Fixture.OpenMigrationContext();
        await using var merchant = MerchantDb(isolated);
        await licensing.GetService<IMigrator>().MigrateAsync(PreviousLicensing);
        await merchant.GetService<IMigrator>().MigrateAsync(PreviousMerchant);
        Assert.Equal(facts, await P0211Tests.OriginalFactsAsync(fixture, owner.ShopId));
        await licensing.Database.MigrateAsync();
        await isolated.PrepareMerchantAsync();
        Assert.Equal(facts, await P0211Tests.OriginalFactsAsync(fixture, owner.ShopId));
        Assert.Equal(1L, await merchant.InventoryBatches.LongCountAsync(batch => batch.ShopId == owner.ShopId));
        Assert.Equal(0L, await fixture.CountAsync("entitlement_snapshots"));
        using var disabled = P0213Transport.Request("{}"u8.ToArray(), EntitlementSnapshotsV1.SnapshotPath, null);
        await ExpectStatusAsync(owner.Client, disabled, HttpStatusCode.Unauthorized);
        await P0209Tests.PostStockAsync(owner.Client, "Stock after observe-only upgrade");
    }

    private async Task AssertRetainedMigrationsAsync(P0213Fixture fixture)
    {
        await using var licensing = fixture.Hosts.Fixture.OpenMigrationContext();
        await using var merchant = MerchantDb(database);
        var source = await Assert.ThrowsAsync<InvalidOperationException>(() => licensing.GetService<IMigrator>().MigrateAsync(PreviousLicensing));
        var destination = await Assert.ThrowsAsync<InvalidOperationException>(() => merchant.GetService<IMigrator>().MigrateAsync(PreviousMerchant));
        Assert.Equal("55000", Assert.IsType<PostgresException>(source.InnerException).SqlState);
        Assert.Equal("55000", Assert.IsType<PostgresException>(destination.InnerException).SqlState);
        // Empty successor migrations may already have rolled back before the populated entitlement migration refuses.
        Assert.Contains("20261116200000_EntitlementDelivery", await licensing.Database.GetAppliedMigrationsAsync());
        await licensing.Database.MigrateAsync();
        Assert.Empty(await licensing.Database.GetPendingMigrationsAsync());
        Assert.Contains("20261118080000_AddLicenceEnforcementFences", await merchant.Database.GetAppliedMigrationsAsync());
        await merchant.Database.MigrateAsync();
        Assert.Empty(await merchant.Database.GetPendingMigrationsAsync());
    }
}
