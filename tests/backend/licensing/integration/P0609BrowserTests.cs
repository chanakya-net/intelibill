using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Shipping admin and merchant web builds in Chromium against the real licensing and merchant APIs on real PostgreSQL.
/// Development simulator receipt fixture only: nothing is dispatched to a payment provider and no live provider is
/// verified. Exercise dropped-response recovery, MFA expiry and a depleted receipt across paged intake.
/// </summary>
public sealed partial class P0609BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Configuration = "../../../tests/backend/licensing/browser/playwright.p0609.config.ts";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [P0609BrowserFact]
    [Trait("Category", "Browser")]
    public async Task FinanceApprovesRefundThroughShippingAdmin()
    {
        var evidence = Environment.GetEnvironmentVariable("P0609_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0609_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);
        var scratch = Directory.CreateTempSubdirectory("intelibill-p0609-");
        try { await RunAsync(evidence, scratch.FullName); }
        finally { scratch.Delete(recursive: true); }
    }

    private async Task RunAsync(string evidence, string scratch)
    {
        await using var fixture = await StartAsync(database, P0609Tests.Instant, browser: true);
        var package = await fixture.PackageAsync(2, P0608Tests.ShopNames);
        var (purchase, term) = await PurchaseAsync(package, "Month");
        await P0608Tests.DrainAsync(fixture);
        var confirmation = await P0608Tests.ConfirmationIdAsync(package.Payer.Client, package.Account, purchase.OrderId);
        var request = await P0609Tests.RequestRefundAsync(package, purchase.OrderId, confirmation, 6000L,
            "Duplicate shop setup before go-live");
        var later = await P0609Tests.RequestRefundAsync(package, purchase.OrderId, confirmation, 1500L,
            "Second branch never opened");
        var before = await P0608Tests.SnapshotAsync(fixture, package, term);
        const string colleague = "other.refunds@example.test";
        var (seed, secondSeed) = await EnrollOperatorsAsync(fixture, colleague);

        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var ct = deadline.Token;
        var merchantRoot = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
        using var staged = new MerchantWebBuild(merchantRoot);
        await using var merchantApi = await P0310ApiRelay.StartAsync(package.Payer.Client.BaseAddress!, ct);
        await using var adminApi = await P0310ApiRelay.StartAsync(fixture.Checkout.Delivery.LicensingApi.BaseAddress!, ct);
        await using var merchant = await StartMerchantWebAsync(staged.Root, merchantApi.Origin, ct);
        await using var admin = await StartAdminWebAsync(adminRoot, adminApi.Origin, ct);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(fixture.Checkout, admin.Origin, ct);
        var targets = new Dictionary<string, string>
        {
            ["ADMIN_WEB"] = admin.Origin, ["MERCHANT_WEB"] = merchant.Origin, ["TEMPORARY"] = scratch,
            ["EVIDENCE_DIR"] = evidence, ["ACCOUNT"] = package.Account.ToString("D"),
            ["ORDER"] = purchase.OrderId.ToString("D"), ["REQUEST"] = request.ToString("D"),
            ["LATER_REQUEST"] = later.ToString("D"),
        };

        // Phase one: preview, approve, lose only the committed response in transit, reload and recover by key.
        await WriteSecretsAsync(scratch, "sessions.json", fixture, package, seed, ct);
        await WriteSecretsAsync(scratch, "second.json", fixture, package, secondSeed, ct, colleague);
        P0609Tests.SetCredentialTime(fixture, fixture.Hosts.Fixture.Clock.GetUtcNow());
        await PlaywrightAsync(adminRoot, targets, "approve", evidence, ct);
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals", $"refund_request_id = '{request:D}' AND amount_paise = 4500"));
        Assert.Equal(1L, await fixture.CountAsync("refund_access_decisions", "treatment = 'Preserve'"));
        var after = await P0608Tests.SnapshotAsync(fixture, package, term);
        Assert.Equal(before, after);

        // Phase two: the same session exactly five minutes after MFA must sign in again before any preview.
        fixture.Hosts.Fixture.Clock.Now = fixture.Hosts.Fixture.Clock.Now.AddMinutes(5);
        await WriteSecretsAsync(scratch, "sessions-mfa.json", fixture, package, seed, ct);
        P0609Tests.SetCredentialTime(fixture, fixture.Hosts.Fixture.Clock.GetUtcNow());
        await PlaywrightAsync(adminRoot, targets, "mfa", evidence, ct);
        Assert.Equal(1L, await fixture.CountAsync("refund_approvals"));
        Assert.Equal(before, await P0608Tests.SnapshotAsync(fixture, package, term));
        await PrepareLedgerAsync(fixture, package, purchase.OrderId, confirmation, targets);
        await PlaywrightAsync(adminRoot, targets, "ledger", evidence, ct);
        Assert.Equal(3L, await fixture.CountAsync("refund_approvals"));
        Assert.Equal(3L, await fixture.CountAsync("refund_access_decisions"));
        Assert.Equal(12000m, await fixture.ScalarAsync("SELECT sum(amount_paise) FROM public.refund_approvals"));
        await RecordAsync(fixture, evidence, merchantRoot, adminRoot, request, before == after, ct);
    }

    private static async Task<(string Seed, string ColleagueSeed)> EnrollOperatorsAsync(P0308Fixture fixture, string colleague)
    {
        var catalogue = fixture.Checkout.Trial.Catalogue;
        using var owner = await catalogue.SignInOwnerAsync();
        var secondSeed = await catalogue.EnrollOperatorAsync(owner, colleague);
        await P0206Fixture.GrantAsync(owner, colleague, "billing");
        var seed = await catalogue.EnrollOperatorAsync(owner, P0306Tests.BillingEmail);
        await P0206Fixture.GrantAsync(owner, P0306Tests.BillingEmail, "billing");
        catalogue.Clock.Advance(TimeSpan.FromSeconds(30));
        return (seed, secondSeed);
    }

    private async Task PlaywrightAsync(string root, Dictionary<string, string> targets, string phase, string evidence,
        CancellationToken ct)
    {
        await RunBrowserAsync(root,targets,phase,evidence,Configuration,"P0609_",output,ct);
    }

    internal static async Task RunBrowserAsync(string root, Dictionary<string,string> targets, string phase, string evidence,
        string config, string prefix, ITestOutputHelper log, CancellationToken ct)
    {
        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", config, "--grep", "@" + phase })
            start.ArgumentList.Add(arg);
        foreach (var (key, value) in targets) start.Environment[prefix + key] = value;
        start.Environment["NODE_PATH"] = Path.Combine(root, "node_modules");
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        log.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(evidence, $"playwright-{phase}.log"),
            report + $"\nexit={browser.Process.ExitCode}\n", ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report);
    }

    internal static async Task WriteSecretsAsync(string scratch, string name, P0308Fixture fixture, Package package,
        string seed, CancellationToken ct, string email = P0306Tests.BillingEmail)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, name), options);
        await JsonSerializer.SerializeAsync(file, new
        {
            admin = email,
            adminPassword = P0114Tests.InviteePassword,
            seed,
            adminTime = fixture.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds(),
            merchant = package.Payer.Email,
            merchantPassword = P0201Tests.Password,
        }, cancellationToken: ct);
    }

    private static async Task RecordAsync(P0308Fixture fixture, string evidence, string merchantRoot, string adminRoot,
        Guid request, bool accessUnchanged, CancellationToken ct)
    {
        using var ngsw = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(merchantRoot, "dist/INVENTORY/browser/ngsw.json"), ct));
        var adminIndexSha = Convert.ToHexStringLower(SHA256.HashData(
            await File.ReadAllBytesAsync(Path.Combine(adminRoot, "dist/ADMIN/browser/index.html"), ct)));
        await File.WriteAllTextAsync(Path.Combine(evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            refundRequestId = request,
            approvals = await fixture.ScalarAsync($"""
                SELECT jsonb_agg(jsonb_build_object('approvalId', approval_id, 'reservationId', reservation_id,
                    'amountPaise', amount_paise, 'remainingBeforePaise', remaining_before_paise,
                    'remainingAfterPaise', remaining_after_paise, 'receiptRevision', receipt_revision))::text
                FROM public.refund_approvals
                """),
            accessDecisions = await fixture.ScalarAsync("""
                SELECT jsonb_agg(jsonb_build_object('decisionId', decision_id, 'approvalId', approval_id,
                    'treatment', treatment, 'shopCount', shop_count, 'effectiveAt', effective_at))::text
                FROM public.refund_access_decisions
                """),
            refundRequests = await fixture.CountAsync("refund_requests"),
            operationalRestrictions = await fixture.CountAsync("operational_restrictions"),
            commercialAndShopFactsEqualBeforeAndAfter = accessUnchanged,
            merchantBuildId = ngsw.RootElement.GetProperty("appData").GetProperty("buildId").GetString(),
            adminIndexSha,
            command = "bun x playwright test --config " + Configuration + " --grep @approve | @mfa | @ledger",
            passed = 3,
            skipped = 0,
            boundary = "Development simulator receipt fixture and shipping local merchant/admin builds only; no provider dispatch and no live payment provider verification.",
        }, Indented), ct);
    }
}

internal sealed class P0609BrowserFactAttribute : FactAttribute
{
    public P0609BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0609_RUN_BROWSER") != "1")
            Skip = "Set P0609_RUN_BROWSER=1 after building both current apps.";
    }
}
