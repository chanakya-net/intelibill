using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0611Tests
{
    private const string PreviousMigration = "20261125140000_CoverageReplacementExceptions";

    [Fact]
    public async Task DocumentBytesAndHashAreImmutableAndDowngradeIsRefused()
    {
        await using var fixture = await StartAsync(database);
        await VerifyEmptyRoundTripAsync(fixture);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        using var billing = await SignInBillingAsync(fixture);
        await AttachAsync(billing, Path(purchase.Quote), Pdf());
        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        var failure = await Record.ExceptionAsync(() => context.GetService<IMigrator>().MigrateAsync(PreviousMigration));
        Assert.Equal("55000", Assert.IsType<PostgresException>(failure?.GetBaseException()).SqlState);
        foreach (var mutation in new[]
        {
            "UPDATE billing_document_versions SET content = '\\x00'", "UPDATE billing_document_versions SET content_sha256 = repeat('a',64)",
            "UPDATE billing_document_versions SET issuer_reference = 'CHANGE'", "DELETE FROM billing_document_versions",
            "TRUNCATE billing_document_versions",
        })
        {
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, mutation));
            Assert.Equal("55000", rejected.SqlState);
        }
        Assert.Equal(Pdf(), await StoredBytesAsync(fixture));
        Assert.Equal(Hash(Pdf()), await ScalarAsync(fixture, "SELECT content_sha256 FROM billing_document_versions"));
    }

    [Fact]
    public async Task NoMerchantSequenceLedgerOrTaxEngineIsReused()
    {
        await using var fixture = await StartAsync(database);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        using var billing = await SignInBillingAsync(fixture);
        var ledgerBefore = await MerchantFactsAsync(fixture);
        var confirmationBefore = await ConfirmationBytesAsync(purchase);
        await AttachAsync(billing, Path(purchase.Quote), Pdf());
        Assert.Equal(ledgerBefore, await MerchantFactsAsync(fixture));
        Assert.Equal(confirmationBefore, await ConfirmationBytesAsync(purchase));
        Assert.True(await ScalarAsync(fixture, "SELECT to_regclass('public.invoice_sequences')::text") is null or DBNull);
        Assert.Equal(0L, await ScalarAsync(fixture, """
            SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public'
                AND table_name = 'billing_document_versions' AND (column_name LIKE '%tax%' OR column_name LIKE '%amount%')
            """));
        ProjectBoundaryAssertions.Verify();
    }

    private static async Task VerifyEmptyRoundTripAsync(P0302Fixture fixture)
    {
        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        Assert.False(context.Database.HasPendingModelChanges());
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        Assert.True(await ScalarAsync(fixture, "SELECT to_regclass('public.billing_document_versions')::text") is null or DBNull);
        await migrator.MigrateAsync();
        Assert.Equal(true, await ScalarAsync(fixture, """
            SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid = 'public.billing_document_versions'::regclass
            """));
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant);
    }

    private static async Task<byte[]> ConfirmationBytesAsync(P0302Fixture.Purchase purchase)
    {
        var path = P0302Fixture.PaymentPath(purchase.Quote) + "/confirmations";
        var entries = await purchase.Payer.Client.GetFromJsonAsync<JsonElement>(path);
        var id = Assert.Single(entries.EnumerateArray()).GetProperty("confirmationId").GetGuid();
        return await purchase.Payer.Client.GetByteArrayAsync($"{path}/{id}/document");
    }

    private static Task<object?> MerchantFactsAsync(P0302Fixture fixture) =>
        fixture.Delivery.MerchantScalarAsync("""
            SELECT concat_ws('|', (SELECT count(*) FROM invoice_sequences), (SELECT count(*) FROM invoice_leases),
                (SELECT count(*) FROM customer_ledger_entries), (SELECT count(*) FROM supplier_ledger_entries),
                (SELECT count(*) FROM payment_receipts))
            """);
}
