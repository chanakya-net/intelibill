using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0307Tests;

namespace Intelibill.Licensing.Integration.Tests;

[Collection(MerchantBillingHosts.Name)]
public sealed class P0307BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Config = "../../tests/licensing/browser/playwright.p0307.config.ts";

    [P0307BrowserFact]
    [Trait("Category", "Browser")]
    public async Task FinanceReviewRecoversOriginalPaymentAndMerchantSeesApplied()
    {
        var evidence = Environment.GetEnvironmentVariable("P0307_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence));
        Directory.CreateDirectory(evidence);
        var scratch = Directory.CreateTempSubdirectory("intelibill-p0307-");
        try
        {
            await RunAsync(evidence, scratch.FullName);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private async Task RunAsync(string evidence, string scratch)
    {
        await using var fixture = await P0308Fixture.StartAsync(database, new DateTimeOffset(2027, 1, 31, 4, 30, 0, TimeSpan.Zero),
            P0306Tests.Settings, browser: true);
        var held = await HoldRenewalAsync(fixture, manual: false);
        await P0302Fixture.CaptureAsync(held.Package.Payer.Client, held.Quote, held.Attempt!.Value);
        P0302Fixture.AssertStatus(await P0302Fixture.VerifyAsync(held.Package.Payer.Client, held.Quote, held.Attempt.Value),
            "Verified", "NeedsReview", "NotApplicable");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = timeout.Token;
        var root = Path.Combine(ProjectPaths.Root, "src/admin-frontend");
        var frontend = Path.Combine(ProjectPaths.Root, "src/frontend");
        using var staged = new MerchantWebBuild(frontend);
        await using var merchant = await StartMerchantWebAsync(staged.Root, held.Package.Payer.Client.BaseAddress!, ct);
        await using var admin = await StartAdminWebAsync(root, fixture.Checkout.Delivery.LicensingApi.BaseAddress!, ct);
        var seed = await P0306Tests.EnrollBillingAsync(fixture.Checkout, P0306Tests.BillingEmail);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(fixture.Checkout, admin.Origin, ct);
        await WriteSecretsAsync(scratch, fixture, held, seed, ct);
        var start = BrowserStart(root, admin.Origin, merchant.Origin, scratch, evidence, held);
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await CoordinateAsync(browser.Process, fixture, held, scratch, ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"),
            $"bun x playwright test --config {Config}\n{report}\nexit={browser.Process.ExitCode}\n", ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal) &&
            !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report);

        await RecordAsync(fixture, held, root, frontend, evidence, ct);
    }

    private static async Task WriteSecretsAsync(string scratch, P0308Fixture fixture, HeldRenewal held, string seed, CancellationToken ct)
    {
        var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, "sessions.json"), fileOptions);
        await JsonSerializer.SerializeAsync(file, new
        {
            admin = P0306Tests.BillingEmail, adminPassword = P0114Tests.InviteePassword, seed,
            adminTime = fixture.Hosts.Fixture.Clock.Now.ToUnixTimeSeconds(), merchant = held.Package.Payer.Email,
            merchantPassword = P0201Tests.Password,
        }, cancellationToken: ct);
    }

    private static ProcessStartInfo BrowserStart(string root, string admin, string merchant, string scratch, string evidence, HeldRenewal held)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Config })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(root, "node_modules");
        start.Environment["P0307_ADMIN_WEB"] = admin;
        start.Environment["P0307_MERCHANT_WEB"] = merchant;
        start.Environment["P0307_TEMPORARY"] = scratch;
        start.Environment["P0307_EVIDENCE_DIR"] = evidence;
        start.Environment["P0307_ACCOUNT"] = held.Package.Account.ToString("D");
        start.Environment["P0307_ORDER"] = held.Quote.OrderId.ToString("D");
        return start;
    }

    private static async Task CoordinateAsync(Process process, P0308Fixture fixture, HeldRenewal held, string scratch, CancellationToken ct)
    {
        if (!await WaitMarkerAsync(process, scratch, "held", ct)) return;
        await WithdrawAsync(held);
        await File.WriteAllTextAsync(Path.Combine(scratch, "withdrawn"), "", ct);
        if (!await WaitMarkerAsync(process, scratch, "age", ct)) return;
        var clock = fixture.Hosts.Fixture.Clock;
        clock.Now = clock.Now.AddMinutes(5);
        fixture.Checkout.Delivery.Transport.LeaseClock.Now = clock.Now.AddMinutes(1);
        await File.WriteAllTextAsync(Path.Combine(scratch, "aged"), clock.Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ct);
        if (!await WaitMarkerAsync(process, scratch, "applying", ct)) return;
        await fixture.Checkout.Delivery.DrainAsync(source: true);
        await fixture.Checkout.Delivery.DrainAsync(source: false);
        await File.WriteAllTextAsync(Path.Combine(scratch, "delivered"), "", ct);
    }

    private static async Task<bool> WaitMarkerAsync(Process process, string scratch, string marker, CancellationToken ct)
    {
        while (!process.HasExited && !File.Exists(Path.Combine(scratch, marker)))
            await Task.Delay(50, ct);
        return !process.HasExited;
    }

    private static async Task RecordAsync(P0308Fixture fixture, HeldRenewal held, string admin, string merchant, string evidence, CancellationToken ct)
    {
        var where = $"order_id = '{held.Quote.OrderId:D}'";
        var verified = await fixture.Checkout.CountAsync("verified_payments", where);
        var attempts = await fixture.Checkout.CountAsync("payment_attempts", where);
        var fulfilments = await fixture.Checkout.CountAsync("payment_fulfilments", where);
        var decisions = await fixture.Checkout.CountAsync("payment_review_decisions", "disposition = 'AcceptedTermsRecovered'");
        Assert.Equal((1L, 1L, 1L, 1L), (verified, attempts, fulfilments, decisions));
        P0302Fixture.AssertStatus(await P0302Fixture.StatusAsync(held.Package.Payer.Client, held.Quote), "Verified", "Fulfilled", "Applied");
        var adminHash = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(admin, "dist/ADMIN/browser/index.html"), ct));
        var merchantHash = SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(merchant, "dist/INVENTORY/browser/ngsw.json"), ct));
        await File.WriteAllTextAsync(Path.Combine(evidence, "browser-postgres.json"), JsonSerializer.Serialize(new
        {
            verified, attempts, fulfilments, decisions, access = "Applied",
            adminIndexSha = Convert.ToHexStringLower(adminHash), merchantManifestSha = Convert.ToHexStringLower(merchantHash),
            boundary = "Real admin and merchant routes and PostgreSQL with the development simulator; browser dropped one decision response.",
        }), ct);
    }
}

internal sealed class P0307BrowserFactAttribute : FactAttribute
{
    public P0307BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0307_RUN_BROWSER") != "1")
            Skip = "Set P0307_RUN_BROWSER=1 after building both shipping apps.";
    }
}
