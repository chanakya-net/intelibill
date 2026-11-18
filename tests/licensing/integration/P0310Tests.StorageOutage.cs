using System.Data.Common;
using System.Net;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310Tests
{
    [Fact]
    public async Task TerminatedReadConnectionReportsUnavailableWithoutInventingPaymentOrAnotherCharge()
    {
        var pause = new PausePaymentRead();
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, Settings(), services =>
            services.ConfigureDbContext<LicensingDbContext>(options => options.AddInterceptors(pause)));
        var purchase = await fixture.AcceptedQuoteAsync(1);
        await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.Created);
        pause.Armed = true;
        var reading = purchase.Payer.Client.GetAsync(PaymentPath(purchase.Quote));
        try
        {
            var pid = await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True((bool)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
                $"SELECT pg_terminate_backend({pid}, 5000)"))!);
        }
        finally { pause.Release.TrySetResult(); }
        using var response = await reading;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Billing.DelegationUnavailable", await P0206Fixture.CodeAsync(response));
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions"));
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        AssertStatus(await StatusAsync(purchase.Payer.Client, purchase.Quote), "AwaitingPayment", "NotFulfilled", "NotApplicable");
    }

    [Fact]
    public async Task HistoricalWorkerSurvivesTerminatedProviderStorageAndRecoversOnItsOriginalPin()
    {
        var pause = new PausePaymentRead("claim_pending_payment");
        var clock = new P0201Clock { Now = WholeSecondStart };
        var settings = Settings();
        settings["Payments:Mode"] = "Disabled";
        settings["Payments:ReconcileInBackground"] = "true";
        await using var fixture = await P0302Fixture.StartAsync(database, clock, settings, services =>
            services.ConfigureDbContext<LicensingDbContext>(options => options.AddInterceptors(pause)));
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        await SelectProfileAsync(owner, "a", 0);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        var attempt = (await StartAsync(purchase.Payer.Client, purchase.Quote, HttpStatusCode.Created)).GetProperty("attemptId").GetGuid();
        await PostAsync(purchase.Payer.Client, CapturePath(purchase.Quote, attempt), new { outcome = "Delayed" });
        await SelectProfileAsync(owner, "b", 1);
        var lifetimes = new[] { fixture.Hosts.Licensing.Services.GetRequiredService<IHostApplicationLifetime>(),
            fixture.Trial.Catalogue.Api.Services.GetRequiredService<IHostApplicationLifetime>() };
        pause.Armed = true;
        clock.Now = clock.Now.AddSeconds(31);
        try
        {
            var pid = await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True((bool)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
                $"SELECT pg_terminate_backend({pid}, 5000)"))!);
        }
        finally { pause.Release.TrySetResult(); }
        await pause.Failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(500);
        Assert.All(lifetimes, lifetime => Assert.False(lifetime.ApplicationStopping.IsCancellationRequested));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && await fixture.CountAsync("verified_payments") == 0) await Task.Delay(100);
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", "provider_account_id = 'simulator-a'"));
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, purchase.Quote, 1);
    }

    private sealed class PausePaymentRead(string target = "FROM public.checkout_orders q") : DbCommandInterceptor
    {
        internal bool Armed { get; set; }
        internal TaskCompletionSource<int> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            Failed.TrySetResult();
            return Task.CompletedTask;
        }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains(target, StringComparison.Ordinal))
            {
                Armed = false;
                Entered.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
}
