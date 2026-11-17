using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Shipping merchant web build in Chromium against real merchant and licensing APIs and PostgreSQL. The Razorpay
/// provider is the in-process fixture transport and the browser's provider script is served by the test: no real
/// Razorpay endpoint is contacted and the fixture proves launcher and server verification, not live acceptance.
/// </summary>
[Collection(MerchantBillingHosts.Name)]
public sealed class P0304BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Configuration = "../../tests/licensing/browser/playwright.p0304.config.ts";
    private static readonly string[] Phases = ["forged", "authorized", "captured", "applied"];

    [P0304BrowserFact]
    [Trait("Category", "Browser")]
    public async Task BillingOwnerPaysThroughRazorpayFixtureAndOnlyServerVerifiedCaptureGrantsAccess()
    {
        var evidence = Environment.GetEnvironmentVariable("P0304_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0304_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        var fake = new FakeRazorpay();
        var clock = new P0201Clock { Now = P0210Fixture.Start };
        fake.Clock = () => clock.Now;
        await using var fixture = await P0302Fixture.StartAsync(database, clock, FakeRazorpay.Settings(),
            services => services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => fake), browser: true);
        var (payer, quote) = await PrepareAsync(fixture);
        var run = new Run(Path.Combine(ProjectPaths.Root, "src/frontend"), evidence, payer.Email, quote);
        using var staged = new MerchantWebBuild(run.Frontend);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var web = await StartMerchantWebAsync(staged.Root, payer.Client.BaseAddress!, timeout.Token);

        await RunAsync(run, web.Origin, "forged", timeout.Token);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(1, fake.Posts);
        var order = fake.OrderId();
        fake.AddPayment(order, "authorized");
        await RunAsync(run, web.Origin, "authorized", timeout.Token);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));

        fake.AddPayment(order, "captured");
        await RunAsync(run, web.Origin, "captured", timeout.Token);
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, quote, 1);
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        await RunAsync(run, web.Origin, "applied", timeout.Token);
        await RecordAsync(fixture, run, fake, timeout.Token);
    }

    private static async Task<(Merchant Payer, CheckoutQuoteV1 Quote)> PrepareAsync(P0302Fixture fixture)
    {
        var payer = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        var account = await CreateAccountAsync(payer.Client, "Razorpay Purchase");
        await P0213Fixture.LinkAsync(account, payer, payer);
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        var plan = await P0301Tests.PublishPlanAsync(fixture.Trial);
        var quote = await P0301Tests.CreateQuoteAsync(payer.Client, account, new(Guid.NewGuid(), plan, "Month", [payer.ShopId], 0, 1));
        using var accepted = await payer.Client.PostAsJsonAsync(P0301Tests.AcceptPath(quote), P0301Tests.Accept(quote));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return (payer, quote);
    }

    private async Task RunAsync(Run run, string origin, string phase, CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Configuration, "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(run.Frontend, "node_modules");
        start.Environment["P0304_MERCHANT_WEB"] = origin;
        start.Environment["P0304_OWNER"] = run.Owner;
        start.Environment["P0304_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0304_EVIDENCE_DIR"] = run.Evidence;
        start.Environment["P0304_ACCOUNT"] = run.Quote.AccountId.ToString("D");
        start.Environment["P0304_ORDER"] = run.Quote.OrderId.ToString("D");
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout;
        output.WriteLine(report);
        var raw = Path.Combine(run.Evidence, "playwright");
        if (Directory.Exists(raw)) Directory.Delete(raw, recursive: true);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, phase + "-playwright.log"), report, ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report + await stderr);
    }

    private static async Task RecordAsync(P0302Fixture fixture, Run run, FakeRazorpay fake, CancellationToken ct)
    {
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(run.Frontend, "dist/INVENTORY/browser/ngsw.json"), ct));
        var git = new ProcessStartInfo("git") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        git.ArgumentList.Add("rev-parse");
        git.ArgumentList.Add("HEAD");
        await using var process = new OwnedProcess(git);
        var sha = (await process.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await process.Process.WaitForExitAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            sha, buildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            amountPaise = run.Quote.AmountPaise, providerOrderPosts = fake.Posts,
            verifiedPayments = await fixture.CountAsync("verified_payments"),
            fulfilments = await fixture.CountAsync("payment_fulfilments"),
            appliedOutbox = await fixture.CountAsync("entitlement_outbox", "status = 'Applied'"),
            command = "bun x playwright test --config " + Configuration, phases = Phases, passed = 4, skipped = 0,
            boundary = "Fake Razorpay HTTP transport and fixture checkout script; no real provider contacted.",
        }), ct);
    }

    private sealed record Run(string Frontend, string Evidence, string Owner, CheckoutQuoteV1 Quote);
}

internal sealed class P0304BrowserFactAttribute : FactAttribute
{
    public P0304BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0304_RUN_BROWSER") != "1")
            Skip = "Set P0304_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
