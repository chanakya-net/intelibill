using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310Tests
{
    [Fact]
    public async Task LegacyConfigurationAndEvidenceSurviveRoutingMigration()
    {
        var legacy = new P0310LegacyRouting();
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, Settings(), services => { legacy.Install(services); P0311LegacyIssuance.InstallLegacy(services); });
        var purchase = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await CaptureAsync(purchase.Payer.Client, purchase.Quote, attempt);
        AssertStatus(await VerifyAsync(purchase.Payer.Client, purchase.Quote, attempt), "Verified", "Fulfilled", "Applying");
        var before = await EvidenceAsync(fixture);
        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        var migration = context.GetService<IMigrator>();
        await migration.MigrateAsync("20261117160000_UnmatchedPaymentReviews");
        Assert.True((bool)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            "SELECT to_regclass('public.payment_provider_routing') IS NULL"))!);
        await migration.MigrateAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, RuntimeGrant);
        Assert.Equal(before, await EvidenceAsync(fixture));
        Assert.False(context.Database.HasPendingModelChanges());
        legacy.BeforeRouting = false;
        var next = await fixture.AcceptedQuoteAsync(1);
        await StartAsync(next.Payer.Client, next.Quote, HttpStatusCode.Created);
        Assert.Equal(1L, await fixture.CountAsync("payment_provider_selections", "provenance = 'DeploymentConfiguration' AND admin_id IS NULL"));
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", "selection_revision = 1"));
        await using var restarted = fixture.Hosts.Licensing.WithWebHostBuilder(_ => { });
        using var scope = restarted.Services.CreateScope();
        var selected = await scope.ServiceProvider.GetRequiredService<Intelibill.Licensing.Application.Payments.IPaymentProviderSelection>()
            .SelectedAsync(false, default);
        Assert.Equal(1, selected.Revision);
        Assert.Equal("legacy-simulator", selected.ProfileId);
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => migration.MigrateAsync("20261117160000_UnmatchedPaymentReviews"));
        Assert.Equal("55000", (refused as PostgresException ?? refused.InnerException as PostgresException)?.SqlState);
        Assert.Equal(before, await EvidenceAsync(fixture, attempt));
    }

    private static Task<object?> EvidenceAsync(P0302Fixture fixture, Guid? attempt = null) =>
        LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner, $"""
            SELECT jsonb_build_object('attempt', to_jsonb(a) - 'selection_revision', 'session', to_jsonb(s),
                'payment', to_jsonb(v), 'fulfilment', to_jsonb(f))::text
            FROM public.payment_attempts a JOIN public.payment_attempt_sessions s USING (attempt_id)
                JOIN public.verified_payments v USING (attempt_id)
                JOIN public.payment_fulfilments f ON f.attempt_id = a.attempt_id
            WHERE {(attempt is { } id ? $"a.attempt_id = '{id:D}'" : "true")}
            """);
}
