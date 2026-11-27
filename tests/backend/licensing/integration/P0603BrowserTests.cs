using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Intelibill.Domain.Enums;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;
using static Intelibill.Licensing.Integration.Tests.P0601Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0603BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Configuration = "../../../tests/backend/licensing/browser/playwright.p0603.config.ts";

    [P0603BrowserTheory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "Browser")]
    public async Task OwnerSchedulesAndRecoversNamedReductionInChromium(bool drift)
    {
        var evidence = Environment.GetEnvironmentVariable("P0603_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0603_EVIDENCE_DIR.");
        evidence = Path.Combine(evidence, drift ? "drift-resolved" : "applied", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);

        await using var fixture = await StartAsync(database, January31, browser: true);
        var package = await fixture.PackageAsync(2, ShopNames);
        var (originalOrder, term) = await PurchaseAsync(package, "Month");
        var delivery = fixture.Checkout.Delivery;
        await delivery.ScanAsync(true);
        await delivery.DrainAsync(true);
        await delivery.DrainAsync(false);

        // Give Shop[1] 4 active members so it displays a live conflict against target allowance 2.
        for (var i = 0; i < 3; i++)
            await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, package.Shops[1].ShopId);

        var solo = await P0603Tests.PublishReductionPlanAsync(fixture, "single-shop", "Solo Renewal", 1, 2, 6000L);
        fixture.At(February14);

        var frontend = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
        using var staged = new MerchantWebBuild(frontend);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        await using var web = await StartMerchantWebAsync(staged.Root, package.Payer.Client.BaseAddress!, timeout.Token);

        var run = new Run(frontend, evidence, package.Payer.Email, package.Account, term, solo,
            package.Shops[0].ShopId, package.Shops[1].ShopId);
        await RunBrowserAsync(run, web.Origin, fixture, package, drift, timeout.Token);

        var orderId = Guid.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "captured.txt"), timeout.Token));
        var schedule = await P0603Tests.ReadScheduleAsync(package, term);
        Assert.Equal("Applied", schedule.State);
        Assert.NotNull(schedule.AppliedTermId);
        Assert.Equal(drift ? 2L : 1L, schedule.ResolutionGeneration);
        Assert.Equal(1L, await fixture.CountAsync("verified_payments", $"order_id = '{orderId:D}'"));
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations", $"order_id = '{orderId:D}'"));
        Assert.Equal(2L, await fixture.CountAsync("subscription_coverage_terms"));

        // Verify merchant projections match licensing authority for both retained and omitted shops.
        Assert.Equal("Active", await delivery.MerchantScalarAsync(
            $"SELECT state FROM shop_license_states WHERE shop_id = '{package.Shops[0].ShopId:D}'"));
        Assert.Equal("Expired", await delivery.MerchantScalarAsync(
            $"SELECT state FROM shop_license_states WHERE shop_id = '{package.Shops[1].ShopId:D}'"));

        await RecordEvidenceAsync(run, originalOrder.OrderId, orderId, schedule.AppliedTermId!.Value, drift, timeout.Token);
        output.WriteLine($"Verified P06-03 browser run (drift={drift}). Order={orderId:D} Successor={schedule.AppliedTermId:D} Evidence={evidence}");
    }

    private async Task RunBrowserAsync(Run run, string origin, P0308Fixture fixture, Package package,
        bool drift, CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = run.Frontend, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Configuration })
            start.ArgumentList.Add(arg);
        start.Environment["P0603_OUTCOME"] = drift ? "drift" : "applied";
        start.Environment["NODE_PATH"] = Path.Combine(run.Frontend, "node_modules");
        start.Environment["P0603_MERCHANT_WEB"] = origin;
        start.Environment["P0603_OWNER"] = run.Owner;
        start.Environment["P0603_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0603_EVIDENCE_DIR"] = run.Evidence;
        start.Environment["P0603_ACCOUNT"] = run.Account.ToString("D");
        start.Environment["P0603_TERM"] = run.Term.ToString("D");
        start.Environment["P0603_TARGET"] = run.Target.ToString("D");
        start.Environment["P0603_RETAINED_SHOP"] = run.RetainedShop.ToString("D");
        start.Environment["P0603_CONFLICTED_SHOP"] = run.ConflictedShop.ToString("D");
        start.Environment["P0603_AMOUNT"] = Rupees(6000L);

        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);

        var capturedFile = Path.Combine(run.Evidence, "captured.txt");
        while (!File.Exists(capturedFile) && !browser.Process.HasExited)
            await Task.Delay(100, ct);

        if (!browser.Process.HasExited)
        {
            var delivery = fixture.Checkout.Delivery;
            if (drift)
            {
                // Admit 2 extra staff on retained shop -> 3 active (> allowance 2), forcing NeedsResolution at renewal!
                var extra1 = await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, package.Shops[0].ShopId);
                await P0209Fixture.SignInAsync(fixture.Hosts, ShopRole.Staff, package.Shops[0].ShopId);
                fixture.At(February29);
                await P0603Tests.DrainReductionAsync(delivery, rounds: 4);
                Assert.Equal("NeedsResolution", (await P0603Tests.ReadScheduleAsync(package, run.Term)).State);
                await File.WriteAllTextAsync(Path.Combine(run.Evidence, "blocked.txt"), "1", ct);

                var readyFile = Path.Combine(run.Evidence, "ready-to-resolve.txt");
                while (!File.Exists(readyFile) && !browser.Process.HasExited)
                    await Task.Delay(100, ct);

                if (!browser.Process.HasExited)
                {
                    using var patch = await P0603Tests.UpdateMemberStatusAsync(package.Shops[0], extra1.UserId, "Suspended", 0);
                    Assert.True(patch.IsSuccessStatusCode, await patch.Content.ReadAsStringAsync(ct));
                    await File.WriteAllTextAsync(Path.Combine(run.Evidence, "staff-resolved.txt"), "1", ct);

                    var resolvedFile = Path.Combine(run.Evidence, "resolved.txt");
                    while (!File.Exists(resolvedFile) && !browser.Process.HasExited)
                        await Task.Delay(100, ct);
                    await P0603Tests.DrainReductionAsync(delivery, rounds: 4);
                }
            }
            else
            {
                fixture.At(February29);
                await P0603Tests.DrainReductionAsync(delivery, rounds: 4);
            }
            await File.WriteAllTextAsync(Path.Combine(run.Evidence, "applied.txt"), "1", ct);
        }

        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout;
        output.WriteLine(report);
        var raw = Path.Combine(run.Evidence, "playwright");
        if (Directory.Exists(raw)) Directory.Delete(raw, recursive: true);
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "capacity-reduction-playwright.log"), report, ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report + await stderr);
    }

    private static async Task RecordEvidenceAsync(Run run, Guid originalOrder, Guid reductionOrder, Guid successorTerm,
        bool drift, CancellationToken ct)
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
            sha,
            buildId = manifest.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            originalOrder,
            predecessorTerm = run.Term,
            reductionOrder,
            successorTerm,
            retainedShop = run.RetainedShop,
            omittedShop = run.ConflictedShop,
            amountPaise = 6000,
            simulatorVerifiedPayments = 1,
            liveProviderVerified = false,
            outcome = drift ? "DriftResolvedAndApplied" : "Applied",
            browser = "Chromium",
            screenshot = "capacity-reduction.png",
            passed = 1,
            skipped = 0,
        };
        await File.WriteAllTextAsync(Path.Combine(run.Evidence, "build-and-postgres.json"), JsonSerializer.Serialize(result), ct);
    }

    private sealed record Run(string Frontend, string Evidence, string Owner, Guid Account, Guid Term, Guid Target,
        Guid RetainedShop, Guid ConflictedShop);
}

internal sealed class P0603BrowserTheoryAttribute : TheoryAttribute
{
    public P0603BrowserTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0603_RUN_BROWSER") != "1")
            Skip = "Set P0603_RUN_BROWSER=1 after building the shipping merchant application.";
    }
}
