using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0607aTests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0607BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private static readonly string[] Checkpoints = ["Preparing", "Scheduled", "Applied"];
    private const string Manager = "browser.transfer@example.test";
    private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };

    [P0607BrowserFact]
    [Trait("Category", "Browser")]
    public async Task ApproveAndRecoverTransferInChromium()
    {
        var evidence = Environment.GetEnvironmentVariable("P0607_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence));
        Directory.CreateDirectory(evidence);
        var scratch = Directory.CreateTempSubdirectory("p0607-browser-");
        using var rsa = RSA.Create(2048);
        await using var s = await StartAsync(database, rsa, browser: true);
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        try { await ExerciseAsync(s, evidence, scratch.FullName, limit.Token); }
        finally { scratch.Delete(true); }
    }

    private async Task ExerciseAsync(Scenario s, string evidence, string scratch, CancellationToken ct)
    {
        var doc = await P0607Tests.AttachOriginalAsync(s);
        var catalogue = s.F.Checkout.Trial.Catalogue;
        using var owner = await catalogue.SignInOwnerAsync();
        var seed = await P0611Tests.EnrollAsync(s.F.Checkout, owner, Manager, "licensing-manager");
        catalogue.Clock.Advance(TimeSpan.FromSeconds(30));
        var merchantRoot = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
        using var staged = new MerchantWebBuild(merchantRoot);
        await using var merchantApi = await P0310ApiRelay.StartAsync(s.Source.Payer.Client.BaseAddress!, ct);
        await using var adminApi = await P0310ApiRelay.StartAsync(s.F.Checkout.Delivery.LicensingApi.BaseAddress!, ct);
        await using var merchant = await StartMerchantWebAsync(staged.Root, merchantApi.Origin, ct);
        await using var admin = await StartAdminWebAsync(adminRoot, adminApi.Origin, ct);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(s.F.Checkout, admin.Origin, ct);
        await WriteFixtureAsync(s, doc, scratch, seed, ct);
        var start = new ProcessStartInfo("bun") { WorkingDirectory = adminRoot, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", "../../../tests/backend/licensing/browser/playwright.p0607.config.ts" })
            start.ArgumentList.Add(arg);
        start.Environment["P0607_TEMPORARY"] = scratch;
        start.Environment["P0607_MERCHANT_WEB"] = merchant.Origin;
        start.Environment["P0607_ADMIN_WEB"] = admin.Origin;
        start.Environment["P0607_EVIDENCE_DIR"] = evidence;
        start.Environment["NODE_PATH"] = Path.Combine(adminRoot, "node_modules");
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await PumpAsync(browser.Process, s, scratch, ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), report, ct);
        output.WriteLine(report);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal), report);
        await P0607Tests.VerifyDocumentsAsync(s, doc);
        await WriteReportAsync(s, evidence, merchantRoot, adminRoot, ct);
    }

    private static async Task PumpAsync(Process process, Scenario s, string scratch, CancellationToken ct)
    {
        async Task<bool> Wait(string name)
        {
            while (!process.HasExited && !File.Exists(Path.Combine(scratch, name))) await Task.Delay(50, ct);
            return !process.HasExited;
        }
        Task Signal(string name) => File.WriteAllTextAsync(Path.Combine(scratch, name), name, ct);
        if (!await Wait("consented")) return;
        await s.F.Checkout.Delivery.DrainAsync(false);
        Assert.Equal(3L, await s.F.CountAsync("coverage_transfer_consents", "kind='Accepted'"));
        await Signal("ready");
        if (!await Wait("preparing")) return;
        Assert.Equal(1L, await s.F.CountAsync("coverage_transfer_approvals"));
        Assert.Equal(0L, await s.F.CountAsync("coverage_transfer_bindings"));
        await s.F.Checkout.Delivery.DrainAsync(true);
        await s.F.Checkout.Delivery.DrainAsync(false);
        await Signal("scheduled");
        if (!await Wait("advance")) return;
        var sessionClock = s.F.Hosts.Fixture.Clock.Now;
        await P0607Tests.AdvanceAsync(s, s.Permit.ValidUntil);
        Assert.Equal("Allowed", await P0606Tests.FencedTradingAtAsync(s.F, s.North.ShopId, s.Permit.ValidUntil));
        s.F.Hosts.Fixture.Clock.Now = sessionClock;
        await Signal("activated");
    }

    private static async Task WriteFixtureAsync(Scenario s, P0607Tests.OriginalDocument doc, string scratch, string seed,
        CancellationToken ct)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, "sessions.json"), options);
        await JsonSerializer.SerializeAsync(file, new
        {
            payer = s.Source.Payer.Email, destination = s.Destination.Payer.Email, shopOwner = s.North.Email,
            merchantPassword = P0201Tests.Password, admin = Manager, adminPassword = P0114Tests.InviteePassword, seed,
            adminTime = s.F.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds(), sourceId = s.Source.Account,
            destinationId = s.Destination.Account, term = s.Term, shop = s.North.ShopId, cutover = s.Permit.ValidUntil,
            order = doc.Order, content = doc.Content, hash = P0611Tests.Hash(P0611Tests.Pdf()),
        }, cancellationToken: ct);
    }

    private static async Task WriteReportAsync(Scenario s, string evidence, string merchant, string admin, CancellationToken ct)
    {
        var active = await s.F.Checkout.Delivery.MerchantScalarAsync(
            $"SELECT account_id FROM public.shop_license_states WHERE shop_id='{s.North.ShopId}'");
        Assert.Equal(s.Destination.Account, active);
        var bindings = await s.F.CountAsync("coverage_transfer_bindings");
        Assert.Equal(1L, bindings);
        await File.WriteAllTextAsync(Path.Combine(evidence, "checkpoints.json"), JsonSerializer.Serialize(new
        {
            scope = "Local simulator, real APIs, PostgreSQL and Chromium", shop = "North", s.Source.Account,
            destination = active, s.Permit.ValidUntil, bindings, states = Checkpoints,
            originalInvoiceSha256 = P0611Tests.Hash(P0611Tests.Pdf()),
            merchantIndexSha256 = P0611Tests.Hash(await File.ReadAllBytesAsync(Path.Combine(merchant, "dist/INVENTORY/browser/index.html"), ct)),
            adminIndexSha256 = P0611Tests.Hash(await File.ReadAllBytesAsync(Path.Combine(admin, "dist/ADMIN/browser/index.html"), ct)),
        }, ReportJson), ct);
    }
}

internal sealed class P0607BrowserFactAttribute : FactAttribute
{
    public P0607BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0607_RUN_BROWSER") != "1") Skip = "Requires both shipping builds and P0607_RUN_BROWSER=1.";
    }
}
