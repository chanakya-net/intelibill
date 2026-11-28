using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

/// <summary>
/// Dual-host shipping admin + merchant web builds in Chromium against real APIs and real PostgreSQL.
/// Development simulator payment and local fixtures support notice scheduling and visibility only;
/// no live email delivery or payment provider verification is claimed.
/// </summary>
public sealed class P0701BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Config = "../../../tests/backend/licensing/browser/playwright.p0701.config.ts";
    private const string BillingAdminEmail = "p0701.browser.billing@example.test";
    private const string SupportAdminEmail = "p0701.browser.support@example.test";
    private const string SecurityAdminEmail = "p0701.browser.security@example.test";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    [P0701BrowserFact]
    [Trait("Category", "Browser")]
    public Task MerchantNoticesFollowLiveShopAndBillingAuthority() =>
        RunBrowserCaseAsync("MerchantNoticesFollowLiveShopAndBillingAuthority", "merchant");

    [P0701BrowserFact]
    [Trait("Category", "Browser")]
    public Task ProviderPreviewUsesLiveCapabilityAndShowsScheduledState() =>
        RunBrowserCaseAsync("ProviderPreviewUsesLiveCapabilityAndShowsScheduledState", "provider");

    private async Task RunBrowserCaseAsync(string grep, string label)
    {
        var evidence = Environment.GetEnvironmentVariable("P0701_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence), "Set P0701_EVIDENCE_DIR.");
        Directory.CreateDirectory(evidence);

        var scratch = Directory.CreateTempSubdirectory($"intelibill-p0701-{label}-");
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        try
        {
            await RunAsync(grep, label, evidence, scratch.FullName, limit.Token);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private async Task RunAsync(string grep, string label, string evidence, string scratch, CancellationToken ct)
    {
        var jan31 = Utc("2028-01-31T04:30:00Z");
        await using var paid = await P0308Fixture.StartAsync(
            database,
            jan31,
            settings: new Dictionary<string, string?>(P0302Fixture.SimulatorSettings)
            {
                ["Database:MaxPoolSize"] = "6",
                ["Notifications:PlanInBackground"] = "false",
            },
            browser: true);

        // Account A: 2 shops on 1 simulator-paid monthly package ending 29 Feb 2028 04:30 UTC (10:00 IST).
        var packageA = await paid.PackageAsync(2, ["North Shop", "South Shop"]);
        var ownerA = packageA.Payer;
        var northShop = packageA.Shops[0];
        var southOwner = packageA.Shops[1];
        var (_, paidTermA) = await PurchaseAsync(packageA, "Month");
        await P0608Tests.DrainAsync(paid);

        // Staff member active in both North Shop and South Shop (for shop-switch A -> B -> A browser verification).
        var staff = await SignInAsync(paid.Hosts, ShopRole.Staff, northShop.ShopId);
        await using (var mdb = MerchantDb(paid.Hosts.Database))
        {
            mdb.ShopMemberships.Add(ShopMembership.Create(southOwner.ShopId, staff.UserId, ShopRole.Staff, isDefault: false));
            await mdb.SaveChangesAsync(ct);
        }

        // Billing-only manager on Account A (zero shop membership).
        var billingManager = await SignInAsync(paid.Hosts);
        await P0701Tests.InviteBillingManagerAsync(paid.Hosts.LicensingOwner, ownerA.Client, packageA.Account, billingManager);

        // Verified opted-in contact user on Account A (no billing role, no shop membership).
        var contact = await SignInAsync(paid.Hosts);
        using (var approved = await ownerA.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{packageA.Account:D}/notice-contacts",
            new ApproveBillingContactRequestV1(Guid.NewGuid(), contact.UserId, contact.Email, "en-IN"),
            ct))
        {
            Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
            var created = (await approved.Content.ReadFromJsonAsync<BillingContactV1>(ct))!;
            using var optedIn = await contact.Client.PostAsJsonAsync(
                $"/api/billing/notice-contacts/{created.ContactId:D}/consent",
                new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), contact.Email, null, OptedIn: true, "en-IN"),
                ct);
            Assert.Equal(HttpStatusCode.OK, optedIn.StatusCode);
        }

        // Account B: separate account, manager, and shop.
        var packageB = await paid.PackageAsync(1, ["Beta Shop"]);
        await PurchaseAsync(packageB, "Month");
        await P0608Tests.DrainAsync(paid);

        // Trial ending 15 Feb 2028 04:30 UTC (10:00 IST) -> 12 Feb and 14 Feb, zero grace.
        paid.At(Utc("2028-02-01T04:30:00Z"));
        var trialOwner = await SignInAsync(paid.Hosts, ShopRole.Owner);
        var trialAccount = await CreateAccountAsync(trialOwner.Client, "Trial Demo Account");
        await P0213Fixture.LinkAsync(trialAccount, trialOwner, trialOwner);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(trialOwner, paid.Checkout.Trial.Accept())).Status);

        // Two concurrent planner scopes + fresh scope restart at 2028-02-01 (all future scheduled rows).
        var concurrent = await Task.WhenAll(
            P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services),
            P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services));
        var restartPass = await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(0, restartPass.Inserted);

        var paidBeforeCount = await CountDueAsync(paid.Hosts.LicensingOwner, packageA.Account, Utc("2028-02-22T04:29:59.999999Z"));
        var paidEqualCount = await CountDueAsync(paid.Hosts.LicensingOwner, packageA.Account, Utc("2028-02-22T04:30:00Z"));
        var paidAfterCount = await CountDueAsync(paid.Hosts.LicensingOwner, packageA.Account, Utc("2028-02-22T04:30:00.000001Z"));
        Assert.Equal(0L, paidBeforeCount);
        Assert.True(paidEqualCount > 0L);
        Assert.Equal(paidEqualCount, paidAfterCount);

        var trialDue = await P0701Tests.ReadScheduledThresholdsAsync(
            paid.Hosts.LicensingOwner,
            trialAccount,
            LicenseNoticesV1.ScopeAccount,
            $"user:{trialOwner.UserId:D}");
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdTrialMinus3Days, Utc("2028-02-12T04:30:00Z")),
                (LicenseNoticesV1.ThresholdTrialMinus1Day, Utc("2028-02-14T04:30:00Z")),
            ],
            trialDue);

        // Advance business clock to 22 Feb 2028 04:30 UTC (10:00 IST): PaidMinus7Days is due; PaidMinus1Day + Grace rows are future.
        paid.At(Utc("2028-02-22T04:30:00Z"));

        var expectedJobIds = await ReadAccountJobIdsAsync(paid.Hosts.LicensingOwner, packageA.Account, ct);
        var jobCountBeforePreview = expectedJobIds.Length;
        Assert.True(jobCountBeforePreview >= 25);

        string billingSeed, supportSeed, securitySeed;
        var catalogue = paid.Checkout.Trial.Catalogue;
        using (var platformOwner = await catalogue.SignInOwnerAsync())
        {
            billingSeed = await P0611Tests.EnrollAsync(paid.Checkout, platformOwner, BillingAdminEmail, "billing");
            supportSeed = await P0611Tests.EnrollAsync(paid.Checkout, platformOwner, SupportAdminEmail, "support");
            securitySeed = await P0611Tests.EnrollAsync(paid.Checkout, platformOwner, SecurityAdminEmail, "security-administrator");
        }

        catalogue.Clock.Advance(TimeSpan.FromSeconds(30));

        var merchantRoot = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
        using var staged = new MerchantWebBuild(merchantRoot);
        await using var merchantApi = await P0310ApiRelay.StartAsync(ownerA.Client.BaseAddress!, ct);
        await using var adminApi = await P0310ApiRelay.StartAsync(paid.Checkout.Delivery.LicensingApi.BaseAddress!, ct);
        await using var merchantWeb = await StartMerchantWebAsync(staged.Root, merchantApi.Origin, ct);
        await using var adminWeb = await StartAdminWebAsync(adminRoot, adminApi.Origin, ct);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(paid.Checkout, adminWeb.Origin, ct);

        await WriteFixtureAsync(
            scratch,
            new
            {
                accountA = packageA.Account,
                accountB = packageB.Account,
                northShopId = northShop.ShopId,
                southShopId = southOwner.ShopId,
                staffEmail = staff.Email,
                southOwnerEmail = southOwner.Email,
                billingManagerEmail = billingManager.Email,
                contactEmail = contact.Email,
                contactAccessToken = contact.Client.DefaultRequestHeaders.Authorization!.Parameter,
                merchantPassword = P0201Tests.Password,
                billingAdminEmail = BillingAdminEmail,
                billingAdminSeed = billingSeed,
                supportAdminEmail = SupportAdminEmail,
                supportAdminSeed = supportSeed,
                securityAdminEmail = SecurityAdminEmail,
                securityAdminSeed = securitySeed,
                adminPassword = P0114Tests.InviteePassword,
                adminTime = paid.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds(),
                expectedJobIds,
            },
            ct);

        var start = BrowserStart(adminRoot, adminWeb.Origin, merchantWeb.Origin, scratch, evidence, grep);
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await WatchCapabilityRevocationAsync(browser.Process, paid, adminWeb.Origin, scratch, ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(evidence, $"playwright-{label}.log"), report + $"\nexit={browser.Process.ExitCode}\n", ct);
        Assert.True(
            browser.Process.ExitCode == 0
                && report.Contains("1 passed", StringComparison.Ordinal)
                && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase),
            report);

        // Confirm preview never wrote new notification_jobs rows.
        Assert.Equal(jobCountBeforePreview, (await ReadAccountJobIdsAsync(paid.Hosts.LicensingOwner, packageA.Account, ct)).Length);

        var adminIndexSha256 = P0611Tests.Hash(await File.ReadAllBytesAsync(
            Path.Combine(adminRoot, "dist/ADMIN/browser/index.html"), ct));
        var merchantIndexSha256 = P0611Tests.Hash(await File.ReadAllBytesAsync(
            Path.Combine(merchantRoot, "dist/INVENTORY/browser/index.html"), ct));

        await File.WriteAllTextAsync(
            Path.Combine(evidence, $"browser-postgres-{label}.json"),
            JsonSerializer.Serialize(
                new
                {
                    boundary = "Shipping admin and merchant web builds, real APIs, real PostgreSQL, local simulator and fixtures only. No live delivery or provider verification.",
                    caseName = grep,
                    accountA = packageA.Account,
                    accountB = packageB.Account,
                    paidTermA,
                    concurrentInserted = concurrent[0].Inserted + concurrent[1].Inserted,
                    restartInserted = restartPass.Inserted,
                    dueCountsAtFeb22 = new { before = paidBeforeCount, equal = paidEqualCount, after = paidAfterCount },
                    trialThresholds = trialDue.Select(item => new { item.Threshold, item.DueAt }),
                    jobCountBeforePreview,
                    jobCountAfterPreview = jobCountBeforePreview,
                    contactJobsPersisted = (await P0701Tests.ReadJobsForRecipientAsync(
                        paid.Hosts.LicensingOwner,
                        packageA.Account,
                        LicenseNoticesV1.ScopeAccount,
                        $"user:{contact.UserId:D}")).Count,
                    adminIndexSha256,
                    merchantIndexSha256,
                },
                Indented),
            ct);
    }

    private static async Task WatchCapabilityRevocationAsync(
        Process process,
        P0308Fixture paid,
        string adminOrigin,
        string scratch,
        CancellationToken ct)
    {
        var trigger = Path.Combine(scratch, "revoke-billing-capability");
        while (!process.HasExited && !File.Exists(trigger))
            await Task.Delay(50, ct);
        if (process.HasExited)
            return;

        await P0306BrowserTests.SetFixtureBrowserOriginAsync(paid.Checkout, P0110CookieBrowser.Origin, ct);
        paid.Checkout.Trial.Catalogue.Clock.Advance(TimeSpan.FromSeconds(30));
        await paid.Checkout.Trial.Catalogue.GrantAsync(BillingAdminEmail, "security-administrator");
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(paid.Checkout, adminOrigin, ct);
        await File.WriteAllTextAsync(Path.Combine(scratch, "billing-revoked"), "revoked", ct);
    }

    private static async Task WriteFixtureAsync(string scratch, object payload, CancellationToken ct)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, "fixture.json"), options);
        await JsonSerializer.SerializeAsync(file, payload, cancellationToken: ct);
    }

    private static ProcessStartInfo BrowserStart(
        string adminRoot,
        string adminOrigin,
        string merchantOrigin,
        string scratch,
        string evidence,
        string grep)
    {
        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = adminRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Config, "--grep", grep })
            start.ArgumentList.Add(arg);
        start.Environment["P0701_ADMIN_WEB"] = adminOrigin;
        start.Environment["P0701_MERCHANT_WEB"] = merchantOrigin;
        start.Environment["P0701_TEMPORARY"] = scratch;
        start.Environment["P0701_EVIDENCE_DIR"] = evidence;
        start.Environment["NODE_PATH"] = Path.Combine(adminRoot, "node_modules");
        return start;
    }

    private static async Task<long> CountDueAsync(
        NpgsqlConnectionStringBuilder settings,
        Guid accountId,
        DateTimeOffset at)
    {
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM public.notification_jobs WHERE account_id = $1 AND due_at <= $2",
            conn);
        cmd.Parameters.AddWithValue(accountId);
        cmd.Parameters.AddWithValue(at);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<Guid[]> ReadAccountJobIdsAsync(
        NpgsqlConnectionStringBuilder settings,
        Guid accountId,
        CancellationToken ct)
    {
        var list = new List<Guid>();
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT id FROM public.notification_jobs WHERE account_id = $1 ORDER BY due_at ASC, id ASC",
            conn);
        cmd.Parameters.AddWithValue(accountId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(reader.GetGuid(0));
        return [.. list];
    }
}

internal sealed class P0701BrowserFactAttribute : FactAttribute
{
    public P0701BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0701_RUN_BROWSER") != "1")
            Skip = "Set P0701_RUN_BROWSER=1 after building both shipping web apps.";
    }
}
