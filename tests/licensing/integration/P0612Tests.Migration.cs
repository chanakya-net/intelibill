using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0612Tests
{
    private const string PreviousMigration = "20261126110000_CoverageTransferProposals";

    [Fact]
    public async Task MemberReadPolicyRoundTripsWithoutModelDrift()
    {
        await using var fixture = await P0611Tests.StartAsync(database);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        using var billing = await P0611Tests.SignInBillingAsync(fixture);
        var pdf = P0611Tests.Pdf();
        var uploaded = await P0611Tests.AttachAsync(billing, P0611Tests.Path(purchase.Quote), pdf);
        var versionId = uploaded.GetProperty("versionId").GetGuid();

        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        Assert.False(context.Database.HasPendingModelChanges());
        var migrator = context.GetService<IMigrator>();

        var before = await ReadPoliciesAsync(fixture);
        Assert.Contains("member_read", before.Keys);
        Assert.Contains("finance_read", before.Keys);
        Assert.Contains("finance_insert", before.Keys);

        await migrator.MigrateAsync(PreviousMigration);
        var down = await ReadPoliciesAsync(fixture);
        Assert.DoesNotContain("member_read", down.Keys);
        Assert.Equal(before["finance_read"], down["finance_read"]);
        Assert.Equal(before["finance_insert"], down["finance_insert"]);
        Assert.Equal(1L, await fixture.CountAsync("billing_document_versions"));

        await migrator.MigrateAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, P0302Fixture.RuntimeGrant);
        var up = await ReadPoliciesAsync(fixture);
        Assert.Contains("member_read", up.Keys);
        Assert.Equal(before["finance_read"], up["finance_read"]);
        Assert.Equal(before["finance_insert"], up["finance_insert"]);

        using var download = await purchase.Payer.Client.GetAsync($"{DocumentsPath(purchase.Quote)}/{versionId:D}/content");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync());
    }

    private static async Task<Dictionary<string, string>> ReadPoliciesAsync(P0302Fixture fixture)
    {
        await using var connection = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT policyname, concat_ws('|', cmd, coalesce(qual, ''), coalesce(with_check, ''))
            FROM pg_policies
            WHERE schemaname = 'public' AND tablename = 'billing_document_versions'
            """, connection);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }
}
