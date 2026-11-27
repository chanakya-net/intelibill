using System.Diagnostics;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0602BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Configuration = "../../tests/licensing/browser/playwright.p0602.config.ts";

    [P0602BrowserTheory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Browser")]
    public async Task OwnerRecoversPaidIncreaseInChromium(bool review)
    {
        var evidence = Environment.GetEnvironmentVariable("P0602_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0602_EVIDENCE_DIR.");
        evidence = Path.Combine(evidence, review ? "review" : "applied", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        await using var fixture = await StartAsync(database, P0601Tests.January31, browser: true);
        var package = await fixture.PackageAsync(2, P0601Tests.ShopNames);
        var (originalOrder, term) = await PurchaseAsync(package, "Month");
        await fixture.Checkout.Delivery.DrainAsync(source: true);
        await fixture.Checkout.Delivery.DrainAsync(source: false);
        var successor = await P0601Tests.RepriceAsync(fixture, package, 18000L);
        var original = await P0308Tests.TermRowAsync(fixture, term);
        fixture.At(P0601Tests.February14);

        await fixture.Checkout.Delivery.ScanAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(true);
        await fixture.Checkout.Delivery.DrainAsync(false);
        var frontend = Path.Combine(ProjectPaths.Root, "src/frontend");
        using var staged = new MerchantWebBuild(frontend);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var web = await StartMerchantWebAsync(staged.Root, package.Payer.Client.BaseAddress!, timeout.Token);
        var run = new Run(frontend, evidence, package.Payer.Email, package.Account, term, successor);
        await RunAsync(run, web.Origin, fixture, review, timeout.Token);
        Assert.Equal(original, await P0308Tests.TermRowAsync(fixture, term));
        var order = Guid.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "captured.txt"), timeout.Token));
        var filter = $"order_id='{order:D}'";
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", filter));
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", filter));
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations", filter));
        Assert.Equal(1L, await fixture.CountAsync("subscription_coverage_terms"));
        var actual = await P0601Tests.AccountTermAsync(package, term);
        Assert.Equal(review ? 3 : 4, actual.GetProperty("purchasedCapacity").GetInt32());
        Assert.Equal(P0601Tests.February29, actual.GetProperty("paidThrough").GetDateTimeOffset());
        await RecordEvidenceAsync(run, originalOrder, order, actual, review, timeout.Token);
        output.WriteLine($"Verified one payment, attempt and confirmation. Capacity {actual.GetProperty("purchasedCapacity")}. Original deadline {P0601Tests.February29:O}. Evidence {evidence}");
    }

    private static async Task RecordEvidenceAsync(Run run, CheckoutQuoteV1 original, Guid order, JsonElement term,
        bool review, CancellationToken ct)
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(run.Frontend, "dist/INVENTORY/browser/ngsw.json"), ct));
        var git = new ProcessStartInfo("git") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        git.ArgumentList.Add("rev-parse");
        git.ArgumentList.Add("HEAD");
        await using var process = new OwnedProcess(git);
        var sha = (await process.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await process.Process.WaitForExitAsync(ct);
        Assert.Equal(0, process.Process.ExitCode);
        var result = new
        {
            sha, buildId = manifest.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            originalOrder = original.OrderId, original.AnchorLocal, original.PreviewEndsAt,
            termId = run.Term, paidThrough = term.GetProperty("paidThrough").GetDateTimeOffset(),
            capacity = term.GetProperty("purchasedCapacity").GetInt32(), increaseOrder = order,
            amountPaise = 10862, simulatorVerifiedPayments = 1, attempts = 1, confirmations = 1, terms = 1,
            disposition = review ? "PaidNeedsReview" : "Applied", browser = "Chromium",
            screenshot = "paid-capacity.png", passed = 1, skipped = 0,
        };
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "build-and-postgres.json"), JsonSerializer.Serialize(result), ct);
    }

    private async Task RunAsync(Run run, string origin, P0308Fixture fixture, bool review, CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Configuration })
            start.ArgumentList.Add(arg);
        start.Environment["P0602_OUTCOME"] = review ? "review" : "applied";
        start.Environment["NODE_PATH"] = Path.Combine(run.Frontend, "node_modules");
        start.Environment["P0602_MERCHANT_WEB"] = origin;
        start.Environment["P0602_OWNER"] = run.Owner;
        start.Environment["P0602_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0602_EVIDENCE_DIR"] = run.Evidence;
        start.Environment["P0602_ACCOUNT"] = run.Account.ToString("D");
        start.Environment["P0602_TERM"] = run.Term.ToString("D");
        start.Environment["P0602_TARGET"] = run.Target.ToString("D");
        start.Environment["P0602_AMOUNT"] = P0601Tests.Rupees(10862L);
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        while (!File.Exists(Path.Combine(run.Evidence, "captured.txt")) && !browser.Process.HasExited)
            await Task.Delay(100, ct);
        if (!browser.Process.HasExited)
        {
            await fixture.Checkout.Delivery.DrainAsync(true);
            await fixture.Checkout.Delivery.DrainAsync(false);
            if (review) fixture.At(P0601Tests.February14.AddMinutes(5));
            await fixture.Checkout.Delivery.DrainAsync(true);
            await fixture.Checkout.Delivery.DrainAsync(false);
        }
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout;
        output.WriteLine(report);
        var raw = Path.Combine(run.Evidence, "playwright");
        if (Directory.Exists(raw)) Directory.Delete(raw, recursive: true);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "paid-capacity-playwright.log"), report, ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report + await stderr);
    }

    private sealed record Run(string Frontend, string Evidence, string Owner, Guid Account, Guid Term, Guid Target);
}

internal sealed class P0602BrowserTheoryAttribute : TheoryAttribute
{
    public P0602BrowserTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0602_RUN_BROWSER") != "1")
            Skip = "Set P0602_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
