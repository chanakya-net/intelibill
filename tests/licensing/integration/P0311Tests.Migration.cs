using System.Net;
using System.Net.Http.Json;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0311Tests
{
    [Fact]
    public async Task HistoricalReceiptsGainTruthfulRetainedConfirmationsAndRefuseDestructiveRollback()
    {
        var legacy = new P0311LegacyIssuance();
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, P0306Tests.Settings, legacy.Install);
        var online = await VerifiedAsync(fixture);
        var manual = await fixture.AcceptedQuoteAsync(1);
        using var billing = await P0306Tests.SignInBillingAsync(fixture);
        using var receipt = await P0212Fixture.PostAsync(billing, P0306Tests.Path(manual.Quote), P0306Tests.Receipt(manual.Quote, WholeSecondStart));
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync("payment_confirmations"));
        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        var migration = context.GetService<IMigrator>();
        const string previous = "20261117170000_PaymentProviderRouting";
        await migration.MigrateAsync(previous);
        await migration.MigrateAsync();
        Assert.False(context.Database.HasPendingModelChanges());
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, RuntimeGrant);
        Assert.Equal(2L, await fixture.CountAsync("payment_confirmations"));
        foreach (var purchase in new[] { online, manual })
        {
            var path = PaymentPath(purchase.Quote) + "/confirmations";
            var row = Assert.Single((await purchase.Payer.Client.GetFromJsonAsync<PaymentConfirmationV1[]>(path))!);
            Assert.True(PaymentConfirmationsV1.IsValid(row, purchase.Account, purchase.Quote.OrderId));
            Assert.Equal("Retained", row.IssuanceBasis);
            Assert.NotEqual(row.VerifiedAt, row.IssuedAt);
            var document = await purchase.Payer.Client.GetStringAsync(path + $"/{row.ConfirmationId}/document");
            Assert.Contains("party details below are current at issuance, not reconstructed historical details", document, StringComparison.Ordinal);
        }
        legacy.BeforeConfirmations = false;
        await VerifiedAsync(fixture);
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations", "facts->'receipt'->>'issuanceBasis' = 'Verification'"));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => migration.MigrateAsync(previous));
        Assert.Equal("55000", (error as PostgresException ?? error.InnerException as PostgresException)?.SqlState);
        Assert.Equal(3L, await fixture.CountAsync("payment_confirmations"));
    }
}
