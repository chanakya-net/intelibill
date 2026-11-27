using System.Diagnostics;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0303BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly string[] Phases = ["uncertain", "applying", "applied"];
    private const string Configuration = "../../../tests/backend/licensing/browser/playwright.p0303.config.ts";

    [P0303BrowserFact]
    [Trait("Category", "Browser")]
    public async Task BillingOwnerRecoversUncertainCheckoutWithoutNewCharge()
    {
        var evidence = Environment.GetEnvironmentVariable("P0303_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0303_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        var clock = new P0201Clock { Now = P0302Fixture.WholeSecondStart };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, browser: true);
        var (payer, _, _, quote) = await fixture.AcceptedQuoteAsync(1);
        var other = await PrepareOtherAccountAsync(fixture, payer, quote);
        var run = new Run(Path.Combine(ProjectPaths.Root, "src/frontend/intelibill"), evidence, payer.Email, quote, other);
        using var built = new MerchantWebBuild(run.Frontend);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        var ct = timeout.Token;
        await using var web = await StartMerchantWebAsync(built.Root, payer.Client.BaseAddress!, ct);

        await RunAsync(run, web.Origin, "uncertain", ct);
        var original = await ReadOriginalAsync(fixture, run, ct);
        await PostAsync(payer.Client, CapturePath(quote, original.Attempt), new { outcome = "Delayed" });
        AssertStatus(await StatusAsync(payer.Client, quote), "PendingVerification", "NotFulfilled", "NotApplicable");
        clock.Now = clock.Now.AddSeconds(30);
        await ObserveRestartedWorkerAsync(fixture, payer, quote, ct);
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, quote, 1);
        await RunAsync(run, web.Origin, "applying", ct);

        await fixture.Delivery.DrainAsync(source: true);
        AssertStatus(await StatusAsync(payer.Client, quote), "Verified", "Fulfilled", "Applying");
        await fixture.Delivery.DrainAsync(source: false);
        await RunAsync(run, web.Origin, "applied", ct);
        await VerifyAndRecordAsync(fixture, payer, run, original, ct);
    }

    private static async Task ObserveRestartedWorkerAsync(P0302Fixture fixture, Merchant payer, CheckoutQuoteV1 quote,
        CancellationToken ct)
    {
        await using var restarted = fixture.Hosts.Licensing.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:ReconcileInBackground"] = "true",
            })));
        _ = restarted.Services;
        for (var count = 0; count < 15; count++)
        {
            var status = await StatusAsync(payer.Client, quote);
            if (status.GetProperty("payment").GetString() == "Verified")
            {
                AssertStatus(status, "Verified", "Fulfilled", "Applying");
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }

        AssertStatus(await StatusAsync(payer.Client, quote), "Verified", "Fulfilled", "Applying");
    }

    private static async Task<CheckoutQuoteV1> PrepareOtherAccountAsync(P0302Fixture fixture, Merchant payer, CheckoutQuoteV1 quote)
    {
        var account = await CreateAccountAsync(payer.Client, "Recovery second account");
        var shop = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        await P0213Fixture.LinkAsync(account, payer, shop);
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        return await P0303Tests.OtherAcceptedQuoteAsync(payer, quote with { AccountId = account }, shop.ShopId);
    }

    private static async Task<Original> ReadOriginalAsync(P0302Fixture fixture, Run run, CancellationToken ct)
    {
        using var observed = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run.Evidence, "uncertain-observations.json"), ct));
        var retained = observed.RootElement.GetProperty("retained");
        var attempt = retained.GetProperty("attempt").GetGuid();
        var key = retained.GetProperty("key").GetGuid();
        var where = $"attempt_id = '{attempt:D}'";
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", $"{where} AND operation_key = '{key:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_sessions"));
        Assert.Equal(0L, await fixture.CountAsync("development_gateway_payments"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_controls", "outcome = 'Timeout'"));
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        var reference = (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            $"SELECT provider_order_reference FROM public.payment_attempt_sessions WHERE {where}"))!;
        return new(attempt, key, reference);
    }

    private async Task RunAsync(Run run, string origin, string phase, CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Configuration, "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(run.Frontend, "node_modules");
        start.Environment["P0303_MERCHANT_WEB"] = origin;
        start.Environment["P0303_OWNER"] = run.Owner;
        start.Environment["P0303_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0303_EVIDENCE_DIR"] = run.Evidence;
        start.Environment["P0303_ACCOUNT"] = run.Quote.AccountId.ToString("D");
        start.Environment["P0303_ORDER"] = run.Quote.OrderId.ToString("D");
        start.Environment["P0303_OTHER_ACCOUNT"] = run.Other.AccountId.ToString("D");
        start.Environment["P0303_OTHER_ORDER"] = run.Other.OrderId.ToString("D");
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout;
        var errors = await stderr;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, phase + "-playwright.log"),
            $"bun x playwright test --config {Configuration} --grep @{phase}\n{report}\n{errors}\nexit={browser.Process.ExitCode}\n", ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal) &&
            !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report + errors);
    }

    private static async Task VerifyAndRecordAsync(P0302Fixture fixture, Merchant payer, Run run, Original original,
        CancellationToken ct)
    {
        var where = $"attempt_id = '{original.Attempt:D}'";
        Assert.Equal(2L, await fixture.CountAsync("payment_attempts"));
        Assert.Equal(2L, await fixture.CountAsync("development_gateway_sessions"));
        Assert.Equal(1L, await fixture.CountAsync("development_gateway_payments"));
        Assert.Equal(original.Reference, await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            $"SELECT provider_order_reference FROM public.payment_attempt_sessions WHERE {where}"));
        Assert.Equal(1L, await fixture.CountAsync("pending_payment_attempts", $"{where} AND completed"));
        AssertStatus(await StatusAsync(payer.Client, run.Quote), "Verified", "Fulfilled", "Applied");
        AssertStatus(await StatusAsync(payer.Client, run.Other), "Failed", "NotFulfilled", "NotApplicable");

        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(run.Frontend, "dist/INVENTORY/browser/ngsw.json"), ct));
        var git = new ProcessStartInfo("git") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        git.ArgumentList.Add("rev-parse");
        git.ArgumentList.Add("HEAD");
        await using var process = new OwnedProcess(git);
        var sha = (await process.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await process.Process.WaitForExitAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            sha, buildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            original.Attempt, original.Key, original.Reference,
            sessionsOnOriginalOrder = 1, separateFailedOrderSessions = 1, capturedPayments = 1, restartedHostedWorker = true,
            verifiedPayments = 1, fulfilments = 1, paidTermFacts = 1, paidShopLinks = 1, acknowledgedShops = 1,
            command = "bun x playwright test --config " + Configuration, phases = Phases,
            passed = 3, skipped = 0, boundary = "Development simulator with real merchant/licensing APIs and PostgreSQL.",
        }), ct);
    }

    private sealed record Run(string Frontend, string Evidence, string Owner, CheckoutQuoteV1 Quote, CheckoutQuoteV1 Other);
    private sealed record Original(Guid Attempt, Guid Key, string Reference);
}

internal sealed class P0303BrowserFactAttribute : FactAttribute
{
    public P0303BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0303_RUN_BROWSER") != "1")
            Skip = "Set P0303_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
