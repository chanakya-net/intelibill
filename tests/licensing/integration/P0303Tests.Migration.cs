using System.Net;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0303Tests
{
    [Fact]
    public async Task MigrationBackfillsExistingAttemptAndPreservesItsOriginalSession()
    {
        const string previous = "20261117090000_PaymentFulfilment";
        var probe = new P0302Tests.GatewayProbe { LoseCreateResponse = true };
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, services: services => { probe.Install(services); P0310LegacyRouting.InstallLegacy(services); });
        var (payer, account, _, quote) = await fixture.AcceptedQuoteAsync(1, legacyQuote: true);
        probe.Database = fixture.Hosts.LicensingOwner;
        probe.Account = account;
        var key = Guid.NewGuid();
        var started = await StartAsync(payer.Client, quote, HttpStatusCode.Created, key);
        var attempt = started.GetProperty("attemptId").GetGuid();

        await using var context = fixture.Hosts.Fixture.OpenMigrationContext();
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        await migrator.MigrateAsync();
        await LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, RuntimeGrant);
        var where = $"attempt_id = '{attempt:D}'";
        Assert.Equal(1L, await fixture.CountAsync("pending_payment_attempts", $"{where} AND attempts = 0 AND NOT completed"));
        await CaptureOriginalSessionAsync(fixture, attempt);

        clock.Now = clock.Now.AddSeconds(30);
        var lease = Assert.IsType<Application.Payments.PendingPaymentLease>(await ClaimAsync(fixture.Hosts.Licensing.Services, clock));
        Assert.Equal(attempt, lease.AttemptId);
        Assert.True(await ApplyLeaseAsync(fixture.Hosts.Licensing.Services, lease));
        AssertStatus(await StatusAsync(payer.Client, quote), "Verified", "Fulfilled", "Applying");
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", $"{where} AND operation_key = '{key:D}'"));
        Assert.Equal(probe.OriginalReference, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            $"SELECT provider_order_reference FROM public.payment_attempt_sessions WHERE {where}"));
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, quote, 1);
    }

    internal static Task CaptureOriginalSessionAsync(P0302Fixture fixture, Guid attempt) =>
        LicensingPostgreSqlFixture.ExecuteAsync(fixture.Hosts.LicensingOwner, $"""
            INSERT INTO public.development_gateway_payments
                (provider_payment_reference, provider_order_reference, amount_paise, currency, state, captured_at)
            SELECT 'recovery_fixture', provider_order_reference, amount_paise, currency, 'Captured', created_at
            FROM public.development_gateway_sessions WHERE attempt_id = '{attempt:D}'
            """);
}
