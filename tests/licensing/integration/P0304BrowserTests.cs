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
public sealed partial class P0304BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Configuration = "../../tests/licensing/browser/playwright.p0304.config.ts";
    private static readonly string[] Phases = ["forged", "authorized", "captured", "applied"];
    private static readonly string[] RecoveryPhases = ["lost", "tabs", "recheck"];
    private static readonly string[] DismissalPhases = ["dismiss-idle", "dismiss-authorized", "dismiss-captured"];

    [P0304BrowserFact]
    [Trait("Category", "Browser")]
    public async Task BillingOwnerPaysThroughRazorpayFixtureAndOnlyServerVerifiedCaptureGrantsAccess()
    {
        await using var session = await Session.StartAsync(database);
        var (fixture, fake, run) = (session.Fixture, session.Fake, session.Run);

        await RunAsync(run, session.Origin, "forged", session.Timeout);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(1, fake.Posts);
        var order = fake.OrderId();
        fake.AddPayment(order, "authorized");
        await RunAsync(run, session.Origin, "authorized", session.Timeout);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));

        fake.AddPayment(order, "captured");
        await RunAsync(run, session.Origin, "captured", session.Timeout);
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, run.Quote, 1);
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        await RunAsync(run, session.Origin, "applied", session.Timeout);
        await RecordAsync(fixture, run, fake, "build-and-postgres.json", Phases, session.Timeout);
    }

    /// <summary>
    /// R004/R010/R012: a lost create response and a failed first verification read recover the same unpaid order; an
    /// older status read from a second tab cannot reopen a newer pending claim; a callback left outstanding across a
    /// change to a second billing account sends nothing; the original order then fulfils through Check again.
    /// </summary>
    [P0304BrowserFact]
    [Trait("Category", "Browser")]
    public async Task OriginalRazorpayOrderRecoversAcrossLostCreateTabsAndAccountChange()
    {
        await using var session = await Session.StartAsync(database, withOtherAccount: true);
        var (fixture, fake, run) = (session.Fixture, session.Fake, session.Run);
        fake.LoseCreateResponse = true;
        fake.FailOrderReads = 1;

        await RunAsync(run, session.Origin, "lost", session.Timeout);
        Assert.Equal(1, fake.Posts);
        Assert.Equal(0, fake.FailOrderReads);
        Assert.Equal(1L, await fixture.CountAsync("payment_attempt_sessions"));
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));

        await RunAsync(run, session.Origin, "tabs", session.Timeout);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("payment_attempts", $"account_id = '{run.Other!.AccountId:D}'"));

        fake.AddPayment(fake.OrderId(), "captured");
        await RunAsync(run, session.Origin, "recheck", session.Timeout);
        Assert.Equal(1, fake.Posts);
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, run.Quote, 1);
        await RecordAsync(fixture, run, fake, "recovery-build-and-postgres.json", RecoveryPhases, session.Timeout);
    }

    /// <summary>
    /// R016: closing the hosted window is an unknown outcome. Before any transaction the proof-free check proves the
    /// original order idle and offers it again; with a payment already authorized it stays pending with no Pay, and
    /// the same attempt is verified once that payment is captured. One provider order throughout.
    /// </summary>
    [P0304BrowserFact]
    [Trait("Category", "Browser")]
    public async Task DismissedRazorpayWindowIsCheckedServerSideBeforePayIsOfferedAgain()
    {
        await using var session = await Session.StartAsync(database);
        var (fixture, fake, run) = (session.Fixture, session.Fake, session.Run);

        await RunAsync(run, session.Origin, "dismiss-idle", session.Timeout);
        Assert.Equal(1, fake.Posts);
        var payment = fake.AddPayment(fake.OrderId(), "authorized");
        await RunAsync(run, session.Origin, "dismiss-authorized", session.Timeout);
        Assert.Equal(1, fake.Posts);
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(0L, await fixture.CountAsync("payment_fulfilments"));

        fake.Transition(payment, "captured");
        await RunAsync(run, session.Origin, "dismiss-captured", session.Timeout);
        Assert.Equal(1, fake.Posts);
        await P0302Tests.AssertSingleFulfilmentAsync(fixture, run.Quote, 1);
        await RecordAsync(fixture, run, fake, "dismissal-build-and-postgres.json", DismissalPhases, session.Timeout);
    }

    internal sealed class Session : IAsyncDisposable
    {
        private readonly CancellationTokenSource timeout = new(TimeSpan.FromMinutes(8));
        private MerchantWebBuild? staged;
        private IAsyncDisposable? web;

        internal required P0302Fixture Fixture { get; init; }
        internal required FakeRazorpay Fake { get; init; }
        internal Run Run { get; private set; } = null!;
        internal string Origin { get; private set; } = "";
        internal CancellationToken Timeout => timeout.Token;

        internal static async Task<Session> StartAsync(LicensingPostgreSqlFixture database, bool withOtherAccount = false,
            string evidenceVariable = "P0304_EVIDENCE_DIR", IReadOnlyDictionary<string, string?>? extraSettings = null)
        {
            var evidence = Environment.GetEnvironmentVariable(evidenceVariable);
            Assert.False(string.IsNullOrWhiteSpace(evidence), $"Set {evidenceVariable}.");
            Directory.CreateDirectory(evidence);
            var fake = new FakeRazorpay();
            var clock = new P0201Clock { Now = P0302Fixture.WholeSecondStart };
            fake.Clock = () => clock.Now;
            var settings = FakeRazorpay.Settings();
            if (extraSettings is not null)
                foreach (var (key, value) in extraSettings) settings[key] = value;
            var fixture = await P0302Fixture.StartAsync(database, clock, settings,
                services => services.AddHttpClient("razorpay-gateway").ConfigurePrimaryHttpMessageHandler(() => fake), browser: true);
            var session = new Session { Fixture = fixture, Fake = fake };
            var (payer, quote) = await PrepareAsync(fixture);
            var other = withOtherAccount ? await OtherAccountQuoteAsync(fixture, payer) : null;
            session.Run = new Run(Path.Combine(ProjectPaths.Root, "src/frontend"), evidence, payer.Email, quote, other);
            session.staged = new MerchantWebBuild(session.Run.Frontend);
            var started = await StartMerchantWebAsync(session.staged.Root, payer.Client.BaseAddress!, session.Timeout);
            (session.web, session.Origin) = (started, started.Origin);
            return session;
        }

        public async ValueTask DisposeAsync()
        {
            if (web is not null)
                await web.DisposeAsync();
            staged?.Dispose();
            await Fixture.DisposeAsync();
            timeout.Dispose();
        }
    }

    /// <summary>A second billing account of the same payer, with its own affiliated shop and accepted quote.</summary>
    private static async Task<CheckoutQuoteV1> OtherAccountQuoteAsync(P0302Fixture fixture, Merchant payer)
    {
        var account = await CreateAccountAsync(payer.Client, "Second Razorpay Account");
        var shop = await SignInAsync(fixture.Hosts, ShopRole.Owner);
        await P0213Fixture.LinkAsync(account, payer, shop);
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        var plan = await P0301Tests.PublishPlanAsync(fixture.Trial);
        var quote = await P0301Tests.CreateQuoteAsync(payer.Client, account, new(Guid.NewGuid(), plan, "Month", [shop.ShopId], 0, 1));
        using var accepted = await payer.Client.PostAsJsonAsync(P0301Tests.AcceptPath(quote), P0301Tests.Accept(quote));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        return quote;
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

    private Task RunAsync(Run run, string origin, string phase, CancellationToken ct) =>
        RunAsync(output, run, origin, phase, Configuration, ct);

    internal static async Task RunAsync(ITestOutputHelper output, Run run, string origin, string phase, string configuration,
        CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", configuration, "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(run.Frontend, "node_modules");
        start.Environment["P0304_MERCHANT_WEB"] = origin;
        start.Environment["P0304_OWNER"] = run.Owner;
        start.Environment["P0304_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0304_EVIDENCE_DIR"] = run.Evidence;
        var scratch = Path.Combine(Path.GetTempPath(), "intelibill-p0304-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        start.Environment["P0304_PLAYWRIGHT_OUTPUT"] = scratch;
        start.Environment["P0304_ACCOUNT"] = run.Quote.AccountId.ToString("D");
        start.Environment["P0304_ORDER"] = run.Quote.OrderId.ToString("D");
        if (run.Other is { } other)
        {
            start.Environment["P0304_OTHER_ACCOUNT"] = other.AccountId.ToString("D");
            start.Environment["P0304_OTHER_ORDER"] = other.OrderId.ToString("D");
        }
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout;
        output.WriteLine(report);
        Directory.Delete(scratch, recursive: true);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, phase + "-playwright.log"), report, ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report + await stderr);
    }

    internal static async Task RecordAsync(P0302Fixture fixture, Run run, FakeRazorpay fake, string file, string[] phases,
        CancellationToken ct, string configuration = Configuration)
    {
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(run.Frontend, "dist/INVENTORY/browser/ngsw.json"), ct));
        var git = new ProcessStartInfo("git") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        git.ArgumentList.Add("rev-parse");
        git.ArgumentList.Add("HEAD");
        await using var process = new OwnedProcess(git);
        var sha = (await process.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await process.Process.WaitForExitAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, file), JsonSerializer.Serialize(new
        {
            sha, buildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            amountPaise = run.Quote.AmountPaise, providerOrderPosts = fake.Posts,
            verifiedPayments = await fixture.CountAsync("verified_payments"),
            fulfilments = await fixture.CountAsync("payment_fulfilments"),
            appliedOutbox = await fixture.CountAsync("entitlement_outbox", "status = 'Applied'"),
            providerCalls = fake.Calls.ToArray(),
            command = "bun x playwright test --config " + configuration, phases, passed = phases.Length, skipped = 0,
            boundary = "Fake Razorpay HTTP transport and fixture checkout script; no real provider contacted.",
        }), ct);
    }

    internal sealed record Run(string Frontend, string Evidence, string Owner, CheckoutQuoteV1 Quote, CheckoutQuoteV1? Other = null);
}

internal sealed class P0304BrowserFactAttribute : FactAttribute
{
    public P0304BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0304_RUN_BROWSER") != "1")
            Skip = "Set P0304_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
