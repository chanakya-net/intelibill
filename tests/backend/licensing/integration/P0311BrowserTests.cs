using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Intelibill.Shared.Contracts.V1;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0311BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [P0311BrowserFact]
    [Trait("Category", "Browser")]
    public async Task MemberDownloadsImmutableConfirmationAndBillingOperatorReadsIdenticalBytes()
    {
        var evidence = Environment.GetEnvironmentVariable("P0311_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence));
        Directory.CreateDirectory(evidence);
        var scratch = Directory.CreateTempSubdirectory("intelibill-p0311-");
        try { await RunAsync(evidence, scratch.FullName); }
        finally { scratch.Delete(recursive: true); }
    }

    private async Task RunAsync(string evidence, string scratch)
    {
        await using var fixture = await P0302Fixture.StartAsync(database, new() { Now = WholeSecondStart }, browser: true);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        var other = await P0209Fixture.CreateAccountAsync(purchase.Payer.Client);
        var seed = await P0306Tests.EnrollBillingAsync(fixture, P0306Tests.BillingEmail);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var ct = deadline.Token;
        var merchantRoot = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
        using var staged = new MerchantWebBuild(merchantRoot);
        await using var merchantApi = await P0310ApiRelay.StartAsync(purchase.Payer.Client.BaseAddress!, ct);
        await using var adminApi = await P0310ApiRelay.StartAsync(fixture.Delivery.LicensingApi.BaseAddress!, ct);
        await using var merchant = await StartMerchantWebAsync(staged.Root, merchantApi.Origin, ct);
        await using var admin = await StartAdminWebAsync(adminRoot, adminApi.Origin, ct);
        await SecretsAsync(scratch, fixture, purchase, seed, ct);
        var start = BrowserStart(adminRoot, admin.Origin, merchant.Origin, scratch, evidence, purchase, other);
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await MutateAfterDownloadAsync(browser.Process, fixture, purchase, scratch, admin.Origin, ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), report + $"\nexit={browser.Process.ExitCode}\n", ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal), report);
        await RecordAsync(fixture, purchase, evidence, adminRoot, merchantRoot, ct);
    }

    private static async Task MutateAfterDownloadAsync(Process process, P0302Fixture fixture, Purchase purchase, string scratch, string adminOrigin, CancellationToken ct)
    {
        while (!process.HasExited && !File.Exists(Path.Combine(scratch, "downloaded"))) await Task.Delay(50, ct);
        if (process.HasExited) return;
        await P0311Tests.MutateAsync(fixture, purchase);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(fixture, adminOrigin, ct);
        await File.WriteAllTextAsync(Path.Combine(scratch, "mutated"), fixture.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
    }

    private static async Task SecretsAsync(string scratch, P0302Fixture fixture, Purchase purchase, string seed, CancellationToken ct)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, "sessions.json"), options);
        await JsonSerializer.SerializeAsync(file, new
        {
            admin = P0306Tests.BillingEmail, adminPassword = P0114Tests.InviteePassword, seed,
            merchant = purchase.Payer.Email, merchantPassword = P0201Tests.Password,
            adminTime = fixture.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds(),
        }, cancellationToken: ct);
    }

    private static ProcessStartInfo BrowserStart(string root, string admin, string merchant, string scratch, string evidence, Purchase purchase, Guid other)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "x", "playwright", "test", "--config", "../../../tests/backend/licensing/browser/playwright.p0311.config.ts" }) start.ArgumentList.Add(argument);
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["ADMIN_WEB"] = admin, ["MERCHANT_WEB"] = merchant, ["TEMPORARY"] = scratch, ["EVIDENCE_DIR"] = evidence,
            ["ACCOUNT"] = purchase.Account.ToString("D"), ["ORDER"] = purchase.Quote.OrderId.ToString("D"), ["OTHER_ACCOUNT"] = other.ToString("D")
        }) start.Environment["P0311_" + key] = value;
        start.Environment["NODE_PATH"] = Path.Combine(root, "node_modules");
        return start;
    }

    private static async Task RecordAsync(P0302Fixture fixture, Purchase purchase, string evidence, string admin, string merchant, CancellationToken ct)
    {
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations"));
        Assert.Equal(1L, await fixture.CountAsync("payment_fulfilments"));
        AssertStatus(await StatusAsync(purchase.Payer.Client, purchase.Quote), "Verified", "Fulfilled", "Applied");
        var row = Assert.Single((await purchase.Payer.Client.GetFromJsonAsync<PaymentConfirmationV1[]>(PaymentPath(purchase.Quote) + "/confirmations", ct))!);
        var bytes = await purchase.Payer.Client.GetByteArrayAsync(PaymentPath(purchase.Quote) + $"/confirmations/{row.ConfirmationId}/document", ct);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(evidence, "merchant-original.txt"), ct));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(evidence, "merchant-after-edits.txt"), ct));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(evidence, "admin-identical.txt"), ct));
        await File.WriteAllTextAsync(Path.Combine(evidence, "browser-postgres.json"), JsonSerializer.Serialize(new
        {
            confirmations = 1, verified = 1, fulfilments = 1, access = "Applied", identicalDownloads = 3,
            documentSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            adminIndexSha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(admin, "dist/ADMIN/browser/index.html"), ct))),
            merchantManifestSha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(merchant, "dist/INVENTORY/browser/ngsw.json"), ct))),
            boundary = "Current merchant/admin builds, real APIs and PostgreSQL; development simulator receipt fixture only. No live provider verification."
        }), ct);
    }
}

internal sealed class P0311BrowserFactAttribute : FactAttribute
{
    public P0311BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0311_RUN_BROWSER") != "1") Skip = "Set P0311_RUN_BROWSER=1 after building both current apps.";
    }
}
