using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Intelibill.Licensing.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0302Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0310BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Config = "../../tests/backend/licensing/browser/playwright.p0310.config.ts";

    [P0310BrowserFact]
    [Trait("Category", "Browser")]
    public async Task OperatorSeesOriginalPinWhileMerchantRecoversAfterSwitch()
    {
        var evidence = Environment.GetEnvironmentVariable("P0310_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence));
        Directory.CreateDirectory(evidence);
        var scratch = Directory.CreateTempSubdirectory("intelibill-p0310-");
        try { await RunAsync(evidence, scratch.FullName); }
        finally { scratch.Delete(recursive: true); }
    }

    private async Task RunAsync(string evidence, string scratch)
    {
        var clock = new P0201Clock { Now = WholeSecondStart };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, P0310Tests.WebhookSettings(), browser: true);
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        using (var initial = await P0212Fixture.PostAsync(owner, "/api/admin/payment-providers/selection",
            new { operationKey = Guid.NewGuid(), expectedRevision = 0, profileId = "a", reason = "Original browser provider A" }))
            Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var purchase = await fixture.AcceptedQuoteAsync(1);
        var nextPurchase = await fixture.AcceptedQuoteAsync(1);
        var otherAccount = await P0209Fixture.CreateAccountAsync(purchase.Payer.Client);
        var seed = await P0306Tests.EnrollBillingAsync(fixture, P0306Tests.BillingEmail);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var ct = timeout.Token;
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/admin-frontend");
        var merchantRoot = Path.Combine(ProjectPaths.Root, "src/frontend");
        using var staged = new MerchantWebBuild(merchantRoot);
        await using var merchantApi = await P0310ApiRelay.StartAsync(purchase.Payer.Client.BaseAddress!, ct);
        await using var adminApi = await P0310ApiRelay.StartAsync(fixture.Delivery.LicensingApi.BaseAddress!, ct);
        await using var merchant = await StartMerchantWebAsync(staged.Root, merchantApi.Origin, ct);
        await using var admin = await StartAdminWebAsync(adminRoot, adminApi.Origin, ct);
        await File.WriteAllTextAsync(Path.Combine(evidence, "fixture-origins.json"), JsonSerializer.Serialize(new
        { admin = admin.Origin, merchant = merchant.Origin, adminApi = adminApi.Origin, merchantApi = merchantApi.Origin }), ct);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(fixture, admin.Origin, ct);
        await WriteSecretsAsync(scratch, fixture, purchase, seed, ct);
        var start = BrowserStart(adminRoot, admin.Origin, merchant.Origin, scratch, evidence, purchase, otherAccount);
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await CoordinateAsync(browser.Process, fixture, purchase, nextPurchase, merchantApi, adminApi, scratch, ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), report + $"\nexit={browser.Process.ExitCode}\n", ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal) &&
            !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report);
        await RecordAsync(fixture, purchase, adminRoot, merchantRoot, evidence, ct);
    }

    private static async Task CoordinateAsync(Process process, P0302Fixture fixture, Purchase purchase, Purchase next, P0310ApiRelay merchantApi, P0310ApiRelay adminApi, string scratch, CancellationToken ct)
    {
        if (!await WaitAsync(process, scratch, "started", ct)) return;
        Assert.Equal(1L, await fixture.CountAsync("payment_attempts", "provider_account_id = 'simulator-a'"));
        if (!await WaitAsync(process, scratch, "selected-b", ct)) return;
        await StartAsync(next.Payer.Client, next.Quote, HttpStatusCode.Created);
        await MarkAsync(scratch, "switched", fixture.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ct);
        if (!await WaitAsync(process, scratch, "inspected", ct)) return;
        await using var unavailable = fixture.Hosts.Licensing.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Payments:Profiles:0:AccountId"] = "missing-a" })));
        unavailable.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        using var unavailableClient = unavailable.CreateClient();
        var unavailableOrigin = ApiOrigin(unavailable.Services);
        Assert.Null(unavailable.Services.GetRequiredService<IPaymentGatewayResolver>().ForPin(P0310Tests.A));
        await using var reopenedMerchant = fixture.Hosts.Merchant.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient("MerchantBillingDelegation").ConfigurePrimaryHttpMessageHandler(() => new P0310LicensingTransport(unavailableOrigin))));
        reopenedMerchant.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, 0));
        using var reopenedClient = reopenedMerchant.CreateClient();
        adminApi.Upstream = unavailableOrigin;
        merchantApi.Upstream = ApiOrigin(reopenedMerchant.Services);
        OutputSafeOrigins(unavailableOrigin, unavailableClient.BaseAddress!, ApiOrigin(reopenedMerchant.Services), reopenedClient.BaseAddress!);
        await MarkAsync(scratch, "missing", "", ct);
        if (!await WaitAsync(process, scratch, "reviewed", ct)) return;
        Assert.Equal(0L, await fixture.CountAsync("verified_payments"));
        Assert.Equal(2L, await fixture.CountAsync("development_gateway_sessions"));
        adminApi.Upstream = fixture.Delivery.LicensingApi.BaseAddress!;
        merchantApi.Upstream = purchase.Payer.Client.BaseAddress!;
        var attempt = (Guid)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            "SELECT attempt_id FROM public.payment_attempts WHERE provider_account_id = 'simulator-a'"))!;
        await CaptureAsync(purchase.Payer.Client, purchase.Quote, attempt);
        var reference = (string)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner,
            $"SELECT provider_order_reference FROM public.payment_attempt_sessions WHERE attempt_id = '{attempt:D}'"))!;
        using var client = fixture.Hosts.Licensing.CreateClient();
        for (var i = 0; i < 2; i++)
        {
            using var receipt = await P0310Tests.SimulatorEventAsync(client, reference, "browser-original-capture", "p0310-simulator-a-webhook-fixture");
            Assert.Equal(HttpStatusCode.NoContent, receipt.StatusCode);
        }
        await P0305Tests.DrainAsync(fixture.Hosts.Licensing.Services, fixture.CheckoutClock);
        await ReceiptCheckpointAsync(fixture, purchase, scratch, "applying", ct);
        await MarkAsync(scratch, "captured", "", ct);
        if (!await WaitAsync(process, scratch, "applying", ct)) return;
        await CompareDownloadsAsync(fixture, purchase, "merchant-applying.txt", "admin-applying.txt");
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        await P0312Tests.AssertDeliveryAsync(fixture, purchase, 1);
        await ReceiptCheckpointAsync(fixture, purchase, scratch, "applied", ct);
        await MarkAsync(scratch, "delivered", "", ct);
    }

    private static Uri ApiOrigin(IServiceProvider services) => new(services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!.Addresses.Single());

    private static void OutputSafeOrigins(params Uri[] origins) => System.Console.WriteLine("P0310 restarted API origins: " + string.Join(", ", origins.Select(uri => uri.GetLeftPart(UriPartial.Authority))));

    private static async Task<bool> WaitAsync(Process process, string scratch, string marker, CancellationToken ct)
    {
        while (!process.HasExited && !File.Exists(Path.Combine(scratch, marker))) await Task.Delay(50, ct);
        return !process.HasExited;
    }

    private static Task MarkAsync(string scratch, string marker, string value, CancellationToken ct) =>
        File.WriteAllTextAsync(Path.Combine(scratch, marker), value, ct);

    private static async Task WriteSecretsAsync(string scratch, P0302Fixture fixture, Purchase purchase, string seed, CancellationToken ct)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, "sessions.json"), options);
        await JsonSerializer.SerializeAsync(file, new
        {
            admin = P0306Tests.BillingEmail, adminPassword = P0114Tests.InviteePassword, seed,
            merchant = purchase.Payer.Email, merchantPassword = P0201Tests.Password,
            ownerEmail = P0112Tests.OwnerEmail, ownerPassword = fixture.Trial.Catalogue.OwnerCredentials.Password,
            ownerSeed = fixture.Trial.Catalogue.OwnerCredentials.Seed,
            adminTime = fixture.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds(),
        }, cancellationToken: ct);
    }

    private static ProcessStartInfo BrowserStart(string root, string admin, string merchant, string scratch, string evidence, Purchase purchase, Guid otherAccount)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "x", "playwright", "test", "--config", Config }) start.ArgumentList.Add(argument);
        foreach (var (key, value) in new Dictionary<string, string>
        {
            ["ADMIN_WEB"] = admin, ["MERCHANT_WEB"] = merchant, ["TEMPORARY"] = scratch, ["EVIDENCE_DIR"] = evidence,
            ["ACCOUNT"] = purchase.Account.ToString("D"), ["ORDER"] = purchase.Quote.OrderId.ToString("D"), ["OTHER_ACCOUNT"] = otherAccount.ToString("D")
        }) start.Environment["P0310_" + key] = value;
        start.Environment["NODE_PATH"] = Path.Combine(root, "node_modules");
        return start;
    }

    private static async Task RecordAsync(P0302Fixture fixture, Purchase purchase, string admin, string merchant, string evidence, CancellationToken ct)
    {
        var attempts = await fixture.CountAsync("payment_attempts");
        var sessions = await fixture.CountAsync("development_gateway_sessions");
        var verified = await fixture.CountAsync("verified_payments");
        var fulfilments = await fixture.CountAsync("payment_fulfilments");
        var selections = await fixture.CountAsync("payment_provider_selections");
        Assert.Equal((2L, 2L, 1L, 1L, 2L), (attempts, sessions, verified, fulfilments, selections));
        AssertStatus(await StatusAsync(purchase.Payer.Client, purchase.Quote), "Verified", "Fulfilled", "Applied");
        await CompareDownloadsAsync(fixture, purchase, "merchant-applying.txt", "admin-applying.txt", "merchant-applied.txt", "admin-applied.txt");
        await File.WriteAllTextAsync(Path.Combine(evidence, "browser-postgres.json"), JsonSerializer.Serialize(new
        {
            attempts, sessions, verified, fulfilments, selections, restartedMissingHost = true, originalPin = P0310Tests.A, currentPin = P0310Tests.B,
            originalCreates = await fixture.CountAsync("development_gateway_sessions", "provider_account_id = 'simulator-a'"),
            currentCreates = await fixture.CountAsync("development_gateway_sessions", "provider_account_id = 'simulator-b'"),
            inboxEvents = await fixture.CountAsync("gateway_inbox_events"),
            adminIndexSha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(admin, "dist/ADMIN/browser/index.html"), ct))),
            merchantManifestSha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(merchant, "dist/INVENTORY/browser/ngsw.json"), ct))),
            boundary = "Current real merchant/admin builds, real APIs and PostgreSQL; development simulator only, no live-provider verification or refund processing."
        }), ct);
    }
}

internal sealed class P0310BrowserFactAttribute : FactAttribute
{
    public P0310BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0310_RUN_BROWSER") != "1") Skip = "Set P0310_RUN_BROWSER=1 after building both shipping apps.";
    }
}
