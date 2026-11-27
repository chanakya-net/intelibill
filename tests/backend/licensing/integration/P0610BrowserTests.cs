using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0609Tests;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0610BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    [P0610BrowserFact]
    [Trait("Category", "Browser")]
    public async Task FinanceAndMerchantRecoverOriginalRefund()
    {
        var evidence = Environment.GetEnvironmentVariable("P0610_EVIDENCE_DIR")!;
        Directory.CreateDirectory(evidence);
        var scratch = Directory.CreateTempSubdirectory("intelibill-refund-browser-");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try { await RunAsync(scratch.FullName, evidence, deadline.Token); }
        finally { scratch.Delete(true); }
    }

    private async Task RunAsync(string scratch, string evidence, CancellationToken ct)
    {
        await using var fixture = await P0308Fixture.StartAsync(database, Instant, P0310Tests.Settings(), browser: true);
        using var owner = await fixture.Checkout.Trial.Catalogue.SignInOwnerAsync();
        await P0310Tests.SelectProfileAsync(owner, "a", 0);
        var (package, request, confirmation, order) = await RequestedAsync(fixture, 6000);
        var failed = await RequestRefundAsync(package, order, confirmation, 2000);
        var seed = await P0306Tests.EnrollBillingAsync(fixture.Checkout, P0306Tests.BillingEmail);
        using var finance = await fixture.Checkout.Trial.Catalogue.SignInOperatorAsync(seed, P0306Tests.BillingEmail);
        SetCredentialTime(fixture, fixture.Checkout.Trial.Catalogue.Clock.GetUtcNow());
        var preview = await PreviewAsync(finance, package.Account, failed, 2000, "Preserve");
        await ReadAsync(await ApproveAsync(finance, package.Account, failed, Command(preview, 2000, "Preserve")), HttpStatusCode.Created);
        await ControlAsync(finance, package.Account, failed, "Failed", P0110CookieBrowser.Origin);
        await P0610Tests.PumpRefundsAsync(fixture);
        var merchantRoot = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
        using var staged = new MerchantWebBuild(merchantRoot);
        await using var merchantApi = await P0310ApiRelay.StartAsync(package.Payer.Client.BaseAddress!, ct);
        await using var adminApi = await P0310ApiRelay.StartAsync(fixture.Checkout.Delivery.LicensingApi.BaseAddress!, ct);
        await using var merchant = await StartMerchantWebAsync(staged.Root, merchantApi.Origin, ct);
        await using var admin = await StartAdminWebAsync(adminRoot, adminApi.Origin, ct);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(fixture.Checkout, admin.Origin, ct);
        // The browser signs in with the next time step of this disposable identity's authenticator.
        fixture.Checkout.Trial.Catalogue.Clock.Advance(TimeSpan.FromSeconds(30));
        SetCredentialTime(fixture, fixture.Hosts.Fixture.Clock.GetUtcNow());
        await P0609BrowserTests.WriteSecretsAsync(scratch, "sessions.json", fixture, package, seed, ct);
        var targets = new Dictionary<string, string>
        {
            ["ADMIN_WEB"] = admin.Origin,
            ["MERCHANT_WEB"] = merchant.Origin,
            ["TEMPORARY"] = scratch,
            ["EVIDENCE_DIR"] = evidence,
            ["ACCOUNT"] = package.Account.ToString(),
            ["REQUEST"] = request.ToString(),
            ["FAILED"] = failed.ToString(),
        };
        var run = P0609BrowserTests.RunBrowserAsync(adminRoot, targets, "recovery", evidence,
            "../../../tests/backend/licensing/browser/playwright.p0610.config.ts", "P0610_", output, ct);
        await WaitForAsync(run, scratch, "approved", ct);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(fixture.Checkout, P0110CookieBrowser.Origin, ct);
        SetCredentialTime(fixture, fixture.Checkout.Trial.Catalogue.Clock.GetUtcNow());
        await ControlAsync(finance, package.Account, request, "Timeout", P0110CookieBrowser.Origin);
        await P0610Tests.PumpRefundsAsync(fixture);
        await P0310Tests.SelectProfileAsync(owner, "b", 1);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(fixture.Checkout, admin.Origin, ct);
        SetCredentialTime(fixture, fixture.Hosts.Fixture.Clock.GetUtcNow());
        await File.WriteAllTextAsync(Path.Combine(scratch, "lost"), "", ct);
        await WaitForAsync(run, scratch, "rechecked", ct);
        fixture.At(Instant.AddDays(2));
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(fixture.Checkout, P0110CookieBrowser.Origin, ct);
        SetCredentialTime(fixture, fixture.Checkout.Trial.Catalogue.Clock.GetUtcNow());
        await ControlAsync(finance, package.Account, request, "Processed", P0110CookieBrowser.Origin);
        await Task.WhenAll(P0610Tests.PumpRefundsAsync(fixture), P0610Tests.PumpRefundsAsync(fixture));
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(fixture.Checkout, admin.Origin, ct);
        SetCredentialTime(fixture, fixture.Hosts.Fixture.Clock.GetUtcNow());
        await File.WriteAllTextAsync(Path.Combine(scratch, "settled"), "", ct);
        await run;
        await RecordAsync(fixture, package, request, evidence, adminRoot, merchantRoot, ct);
    }

    private static async Task WaitForAsync(Task browser, string directory, string signal, CancellationToken ct)
    {
        while (!File.Exists(Path.Combine(directory, signal)))
        {
            if (browser.IsCompleted) { await browser; Assert.Fail("Browser ended before " + signal); }
            await Task.Delay(100, ct);
        }
    }

    private static async Task ControlAsync(P0110CookieBrowser finance, Guid account, Guid request, string outcome, string origin)
    {
        using var result = await P0212Fixture.PostAsync(finance, RequestPath(account, request) + "/simulator", new { outcome }, origin);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }

    private static async Task RecordAsync(P0308Fixture fixture, P0308Fixture.Package package, Guid request, string evidence, string admin,
        string merchant, CancellationToken ct)
    {
        var term = await P0308Fixture.ShopTermAsync(package, package.Shops[0]);
        Assert.Equal("Expired", term.GetProperty("state").GetString());
        var remoteA = await fixture.CountAsync("development_gateway_refunds", "provider_account_id='simulator-a'");
        var remoteB = await fixture.CountAsync("development_gateway_refunds", "provider_account_id='simulator-b'");
        Assert.Equal((2L, 0L), (remoteA, remoteB));
        var sends = await fixture.CountAsync("refund_sends");
        var applications = await fixture.CountAsync("refund_applications");
        Assert.Equal((2L, 1L), (sends, applications));
        var requestScope = $"reservation_id IN (SELECT reservation_id FROM refund_attempts WHERE refund_request_id='{request:D}')";
        Assert.Equal(1L, await fixture.CountAsync("refund_sends", requestScope));
        var reads = await fixture.CountAsync("refund_observations", requestScope + " AND reason='OriginalProviderRead'");
        Assert.Equal(1L, reads);
        var snapshots = await fixture.CountAsync("entitlement_snapshots");
        var failureHolds = await fixture.CountAsync("refund_work", "state='Failed'");
        Assert.Equal(1L, failureHolds);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(merchant, "dist/INVENTORY/browser/ngsw.json"), ct));
        await File.WriteAllTextAsync(Path.Combine(evidence, "refund-demo.json"), JsonSerializer.Serialize(new
        {
            provenance = "Local PostgreSQL development simulator, disposable fixture identities, real Chromium and shipping builds",
            confirmedPaise = 4500,
            failedPaise = 2000,
            requestedPaise = 6000,
            remoteA,
            remoteB,
            sends,
            applications,
            recoveredRequestSends = 1,
            verifiedOriginalReads = reads,
            snapshots,
            failureHolds,
            observedAccessEndsAt = term.GetProperty("accessEndsAt"),
            merchantBuild = manifest.RootElement.GetProperty("appData").GetProperty("buildId"),
            adminIndexSha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(admin, "dist/ADMIN/browser/index.html"), ct))),
        }), ct);
    }
}

internal sealed class P0610BrowserFactAttribute : FactAttribute
{
    public P0610BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0610_RUN_BROWSER") != "1") Skip = "Requires shipping Chromium fixtures.";
    }
}
