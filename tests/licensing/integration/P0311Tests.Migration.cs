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
    public async Task QuiescedCutoverDrainsLegacyWritersBackfillsChecksCompletenessThenResumes()
    {
        var legacy = new P0311LegacyIssuance();
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, P0306Tests.Settings, legacy.Install);
        var online = await VerifiedAsync(fixture);
        var manual = await fixture.AcceptedQuoteAsync(1);
        using var billing = await P0306Tests.SignInBillingAsync(fixture);
        using var receipt = await P0212Fixture.PostAsync(billing, P0306Tests.Path(manual.Quote), P0306Tests.Receipt(manual.Quote, WholeSecondStart));
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        Assert.Equal(0L, await fixture.CountAsync("payment_confirmations"));
        // Both old-writer calls have completed. No receipt writer runs during the cutover.
        var gap = await Assert.ThrowsAsync<PostgresException>(() => CheckCompletenessAsync(fixture));
        Assert.Equal("23514", gap.SqlState);
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
        legacy.BeforeConfirmations = false; // All replacement writers are confirmation-aware, ingress still paused.
        await CheckCompletenessAsync(fixture); // Zero missing receipts is the resume gate.
        await VerifiedAsync(fixture);
        var resumedManual = await fixture.AcceptedQuoteAsync(1);
        using var resumed = await P0212Fixture.PostAsync(billing, P0306Tests.Path(resumedManual.Quote),
            P0306Tests.Receipt(resumedManual.Quote, WholeSecondStart));
        Assert.Equal(HttpStatusCode.Created, resumed.StatusCode);
        await CheckCompletenessAsync(fixture);
        Assert.Equal(2L, await fixture.CountAsync("payment_confirmations", "facts->'receipt'->>'issuanceBasis' = 'Verification'"));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => migration.MigrateAsync(previous));
        Assert.Equal("55000", (error as PostgresException ?? error.InnerException as PostgresException)?.SqlState);
        Assert.Equal(4L, await fixture.CountAsync("payment_confirmations"));
    }

    [Fact]
    public async Task CompletenessGateRefusesOldWriterRollbackWithoutChangingIssuedFacts()
    {
        var legacy = new P0311LegacyIssuance { BeforeConfirmations = false };
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, P0306Tests.Settings, legacy.Install);
        var issued = await VerifiedAsync(fixture);
        var path = PaymentPath(issued.Quote) + "/confirmations";
        var row = Assert.Single((await issued.Payer.Client.GetFromJsonAsync<PaymentConfirmationV1[]>(path))!);
        var original = await issued.Payer.Client.GetByteArrayAsync(path + $"/{row.ConfirmationId}/document");
        await CheckCompletenessAsync(fixture);
        legacy.BeforeConfirmations = true; // Deliberately unsupported rollback: schema alone cannot protect issuance.
        await VerifiedAsync(fixture);
        var manual = await fixture.AcceptedQuoteAsync(1);
        using var billing = await P0306Tests.SignInBillingAsync(fixture);
        using var receipt = await P0212Fixture.PostAsync(billing, P0306Tests.Path(manual.Quote), P0306Tests.Receipt(manual.Quote, WholeSecondStart));
        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        var error = await Assert.ThrowsAsync<PostgresException>(() => CheckCompletenessAsync(fixture));
        Assert.Equal("23514", error.SqlState);
        Assert.Equal(original, await issued.Payer.Client.GetByteArrayAsync(path + $"/{row.ConfirmationId}/document"));
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations"));
    }

    private static async Task CheckCompletenessAsync(P0302Fixture fixture)
    {
        try
        {
            await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner,
                File.ReadAllText(Path.Combine(ProjectPaths.Root, "scripts/db/check-payment-confirmation-completeness.sql")));
        }
        finally
        {
            Assert.True((bool)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
                "SELECT count(*) = 3 AND bool_and(relforcerowsecurity) FROM pg_class WHERE oid IN ('public.verified_payments'::regclass, 'public.manual_payment_receipts'::regclass, 'public.payment_confirmations'::regclass)"))!);
        }
    }
}
