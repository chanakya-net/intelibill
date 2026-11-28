using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
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

        var seeded = await SeedAccountsAndScheduleAsync(paid, ct);
        var admins = await EnrollAdminsAsync(paid);
        var merchantRoot = Path.Combine(ProjectPaths.Root, "src/frontend/intelibill");
        var adminRoot = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");

        using var staged = new MerchantWebBuild(merchantRoot);
        await using var merchantApi = await P0310ApiRelay.StartAsync(seeded.PackageA.Payer.Client.BaseAddress!, ct);
        await using var adminApi = await P0310ApiRelay.StartAsync(paid.Checkout.Delivery.LicensingApi.BaseAddress!, ct);
        await using var merchantWeb = await StartMerchantWebAsync(staged.Root, merchantApi.Origin, ct);
        await using var adminWeb = await StartAdminWebAsync(adminRoot, adminApi.Origin, ct);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(paid.Checkout, adminWeb.Origin, ct);

        await WriteFixtureAsync(scratch, BuildFixturePayload(paid, seeded, admins), ct);
        await RunPlaywrightAndCheckAsync(paid, seeded, admins, adminRoot, adminWeb.Origin, merchantWeb.Origin, scratch, evidence, grep, label, ct);
        await WritePostgresEvidenceAsync(paid, seeded, merchantRoot, adminRoot, evidence, grep, label, ct);
    }

    private static async Task<SeededBrowserData> SeedAccountsAndScheduleAsync(P0308Fixture paid, CancellationToken ct)
    {
        var packageA = await paid.PackageAsync(2, ["North Shop", "South Shop"]);
        var (_, paidTermA) = await PurchaseAsync(packageA, "Month");
        await P0608Tests.DrainAsync(paid);

        var staff = await SignInAsync(paid.Hosts, ShopRole.Staff, packageA.Shops[0].ShopId);
        await using (var mdb = MerchantDb(paid.Hosts.Database))
        {
            mdb.ShopMemberships.Add(ShopMembership.Create(packageA.Shops[1].ShopId, staff.UserId, ShopRole.Staff, isDefault: false));
            await mdb.SaveChangesAsync(ct);
        }

        var billingManager = await SignInAsync(paid.Hosts);
        await P0701Tests.InviteBillingManagerAsync(paid.Hosts.LicensingOwner, packageA.Payer.Client, packageA.Account, billingManager);
        var contact = await SeedOptedInContactAsync(paid, packageA, ct);

        var packageB = await paid.PackageAsync(1, ["Beta Shop"]);
        await PurchaseAsync(packageB, "Month");
        await P0608Tests.DrainAsync(paid);

        paid.At(Utc("2028-02-01T04:30:00Z"));
        var trialOwner = await SignInAsync(paid.Hosts, ShopRole.Owner);
        var trialAccount = await CreateAccountAsync(trialOwner.Client, "Trial Demo Account");
        await P0213Fixture.LinkAsync(trialAccount, trialOwner, trialOwner);
        Assert.Equal("Applied", (await P0210Tests.StartAndWaitAsync(trialOwner, paid.Checkout.Trial.Accept())).Status);

        var concurrent = await Task.WhenAll(
            P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services),
            P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services));
        var freshScopeReplay = await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        Assert.Equal(0, freshScopeReplay.Inserted);

        var paidBefore = await CountDueAsync(paid.Hosts.LicensingOwner, packageA.Account, Utc("2028-02-22T04:29:59.999999Z"));
        var paidEqual = await CountDueAsync(paid.Hosts.LicensingOwner, packageA.Account, Utc("2028-02-22T04:30:00Z"));
        var paidAfter = await CountDueAsync(paid.Hosts.LicensingOwner, packageA.Account, Utc("2028-02-22T04:30:00.000001Z"));
        Assert.Equal(0L, paidBefore);
        Assert.True(paidEqual > 0L);
        Assert.Equal(paidEqual, paidAfter);

        var trialDue = await P0701Tests.ReadScheduledThresholdsAsync(
            paid.Hosts.LicensingOwner, trialAccount, LicenseNoticesV1.ScopeAccount, $"user:{trialOwner.UserId:D}");
        Assert.Equal(
            [
                (LicenseNoticesV1.ThresholdTrialMinus3Days, Utc("2028-02-12T04:30:00Z")),
                (LicenseNoticesV1.ThresholdTrialMinus1Day, Utc("2028-02-14T04:30:00Z")),
            ],
            trialDue);

        paid.At(Utc("2028-02-22T04:30:00Z"));
        var expectedJobIds = await ReadAccountJobIdsAsync(paid.Hosts.LicensingOwner, packageA.Account, ct);
        Assert.True(expectedJobIds.Length >= 25);

        return new SeededBrowserData(
            packageA, packageB, paidTermA, staff, billingManager, contact,
            concurrent[0].Inserted + concurrent[1].Inserted, freshScopeReplay.Inserted,
            paidBefore, paidEqual, paidAfter, trialDue, expectedJobIds);
    }

    private static async Task<Merchant> SeedOptedInContactAsync(P0308Fixture paid, P0308Fixture.Package packageA, CancellationToken ct)
    {
        var contact = await SignInAsync(paid.Hosts);
        using var approved = await packageA.Payer.Client.PostAsJsonAsync(
            $"/api/billing/accounts/{packageA.Account:D}/notice-contacts",
            new ApproveBillingContactRequestV1(Guid.NewGuid(), contact.UserId, contact.Email, "en-IN"),
            ct);
        Assert.Equal(HttpStatusCode.Created, approved.StatusCode);
        var created = (await approved.Content.ReadFromJsonAsync<BillingContactV1>(ct))!;
        using var optedIn = await contact.Client.PostAsJsonAsync(
            $"/api/billing/notice-contacts/{created.ContactId:D}/consent",
            new ConfirmBillingContactConsentRequestV1(Guid.NewGuid(), contact.Email, null, OptedIn: true, "en-IN"),
            ct);
        Assert.Equal(HttpStatusCode.OK, optedIn.StatusCode);
        return contact;
    }

    private static async Task<BrowserAdmins> EnrollAdminsAsync(P0308Fixture paid)
    {
        var catalogue = paid.Checkout.Trial.Catalogue;
        using var platformOwner = await catalogue.SignInOwnerAsync();
        var billingSeed = await P0611Tests.EnrollAsync(paid.Checkout, platformOwner, BillingAdminEmail, "billing");
        var supportSeed = await P0611Tests.EnrollAsync(paid.Checkout, platformOwner, SupportAdminEmail, "support");
        var securitySeed = await P0611Tests.EnrollAsync(paid.Checkout, platformOwner, SecurityAdminEmail, "security-administrator");
        catalogue.Clock.Advance(TimeSpan.FromSeconds(30));
        return new BrowserAdmins(billingSeed, supportSeed, securitySeed);
    }

    private static object BuildFixturePayload(P0308Fixture paid, SeededBrowserData seeded, BrowserAdmins admins) =>
        new
        {
            accountA = seeded.PackageA.Account,
            accountB = seeded.PackageB.Account,
            northShopId = seeded.PackageA.Shops[0].ShopId,
            southShopId = seeded.PackageA.Shops[1].ShopId,
            staffEmail = seeded.Staff.Email,
            southOwnerEmail = seeded.PackageA.Shops[1].Email,
            billingManagerEmail = seeded.BillingManager.Email,
            contactEmail = seeded.Contact.Email,
            contactAccessToken = seeded.Contact.Client.DefaultRequestHeaders.Authorization!.Parameter,
            merchantPassword = P0201Tests.Password,
            billingAdminEmail = BillingAdminEmail,
            billingAdminSeed = admins.BillingSeed,
            supportAdminEmail = SupportAdminEmail,
            supportAdminSeed = admins.SupportSeed,
            securityAdminEmail = SecurityAdminEmail,
            securityAdminSeed = admins.SecuritySeed,
            adminPassword = P0114Tests.InviteePassword,
            adminTime = paid.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds(),
            expectedJobIds = seeded.ExpectedJobIds,
        };

    private async Task RunPlaywrightAndCheckAsync(
        P0308Fixture paid,
        SeededBrowserData seeded,
        BrowserAdmins admins,
        string adminRoot,
        string adminOrigin,
        string merchantOrigin,
        string scratch,
        string evidence,
        string grep,
        string label,
        CancellationToken ct)
    {
        var start = BrowserStart(adminRoot, adminOrigin, merchantOrigin, scratch, evidence, grep);
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await WatchBrowserSignalsAsync(browser.Process, paid, seeded, admins, adminOrigin, scratch, ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout + await stderr;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(evidence, $"playwright-{label}.log"), report + $"\nexit={browser.Process.ExitCode}\n", ct);
        Assert.True(
            browser.Process.ExitCode == 0
                && report.Contains("1 passed", StringComparison.Ordinal)
                && !report.Contains("skipped", StringComparison.OrdinalIgnoreCase),
            report);

        var finalJobIds = await ReadAccountJobIdsAsync(paid.Hosts.LicensingOwner, seeded.PackageA.Account, ct);
        if (label == "provider")
            Assert.Equal(seeded.ExpectedJobIds.Length, finalJobIds.Length);
        else
            Assert.All(seeded.ExpectedJobIds, id => Assert.Contains(id, finalJobIds));
    }

    private static async Task WatchBrowserSignalsAsync(
        Process process,
        P0308Fixture paid,
        SeededBrowserData seeded,
        BrowserAdmins admins,
        string adminOrigin,
        string scratch,
        CancellationToken ct)
    {
        Guid? activeNorthRestriction = null;
        while (!process.HasExited)
        {
            await HandleClockSignalAsync(paid, scratch, ct);
            activeNorthRestriction = await HandleSuspensionSignalsAsync(paid, seeded, admins, adminOrigin, scratch, activeNorthRestriction, ct);
            await HandleRenewalSignalAsync(paid, seeded, scratch, ct);
            await HandleCapabilityRevocationSignalAsync(paid, adminOrigin, scratch, ct);
            await Task.Delay(50, ct);
        }
    }

    private static async Task HandleClockSignalAsync(P0308Fixture paid, string scratch, CancellationToken ct)
    {
        var req = Path.Combine(scratch, "advance-clock.req");
        if (!File.Exists(req))
            return;
        var text = (await File.ReadAllTextAsync(req, ct)).Trim();
        File.Delete(req);
        paid.At(DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal));
        await File.WriteAllTextAsync(Path.Combine(scratch, "advance-clock.ack"), text, ct);
    }

    private static async Task<Guid?> HandleSuspensionSignalsAsync(
        P0308Fixture paid,
        SeededBrowserData seeded,
        BrowserAdmins admins,
        string adminOrigin,
        string scratch,
        Guid? currentRestriction,
        CancellationToken ct)
    {
        var suspendReq = Path.Combine(scratch, "suspend-north.req");
        if (File.Exists(suspendReq))
        {
            File.Delete(suspendReq);
            await P0306BrowserTests.SetFixtureBrowserOriginAsync(paid.Checkout, P0110CookieBrowser.Origin, ct);
            paid.Checkout.Trial.Catalogue.Clock.Advance(TimeSpan.FromSeconds(30));
            using var sec = await paid.Checkout.Trial.Catalogue.SignInOperatorAsync(admins.SecuritySeed, SecurityAdminEmail);
            currentRestriction = await P0404Tests.SuspendAsync(
                sec, $"/api/admin/accounts/{seeded.PackageA.Account:D}/restrictions", "Shop", seeded.PackageA.Shops[0].ShopId);
            await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
            await P0306BrowserTests.SetFixtureBrowserOriginAsync(paid.Checkout, adminOrigin, ct);
            await File.WriteAllTextAsync(Path.Combine(scratch, "suspend-north.ack"), currentRestriction.Value.ToString("D"), ct);
        }

        var releaseReq = Path.Combine(scratch, "release-north.req");
        if (File.Exists(releaseReq) && currentRestriction is { } id)
        {
            File.Delete(releaseReq);
            await P0306BrowserTests.SetFixtureBrowserOriginAsync(paid.Checkout, P0110CookieBrowser.Origin, ct);
            paid.Checkout.Trial.Catalogue.Clock.Advance(TimeSpan.FromSeconds(30));
            using var sec = await paid.Checkout.Trial.Catalogue.SignInOperatorAsync(admins.SecuritySeed, SecurityAdminEmail);
            await P0701Tests.ReleaseRestrictionAsync(sec, seeded.PackageA.Account, id);
            await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
            await P0306BrowserTests.SetFixtureBrowserOriginAsync(paid.Checkout, adminOrigin, ct);
            await File.WriteAllTextAsync(Path.Combine(scratch, "release-north.ack"), "released", ct);
            return null;
        }

        return currentRestriction;
    }

    private static async Task HandleRenewalSignalAsync(P0308Fixture paid, SeededBrowserData seeded, string scratch, CancellationToken ct)
    {
        var req = Path.Combine(scratch, "renew-account-a.req");
        if (!File.Exists(req))
            return;
        File.Delete(req);
        await RenewAsync(seeded.PackageA, seeded.PaidTermA, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        await File.WriteAllTextAsync(Path.Combine(scratch, "renew-account-a.ack"), "renewed", ct);
    }

    private static async Task HandleCapabilityRevocationSignalAsync(
        P0308Fixture paid,
        string adminOrigin,
        string scratch,
        CancellationToken ct)
    {
        var trigger = Path.Combine(scratch, "revoke-billing-capability");
        if (!File.Exists(trigger))
            return;
        File.Delete(trigger);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(paid.Checkout, P0110CookieBrowser.Origin, ct);
        paid.Checkout.Trial.Catalogue.Clock.Advance(TimeSpan.FromSeconds(30));
        await paid.Checkout.Trial.Catalogue.GrantAsync(BillingAdminEmail, "security-administrator");
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(paid.Checkout, adminOrigin, ct);
        await File.WriteAllTextAsync(Path.Combine(scratch, "billing-revoked"), "revoked", ct);
    }

    private static async Task WritePostgresEvidenceAsync(
        P0308Fixture paid,
        SeededBrowserData seeded,
        string merchantRoot,
        string adminRoot,
        string evidence,
        string grep,
        string label,
        CancellationToken ct)
    {
        var finalJobCount = (await ReadAccountJobIdsAsync(paid.Hosts.LicensingOwner, seeded.PackageA.Account, ct)).Length;
        var adminIndexSha256 = P0611Tests.Hash(await File.ReadAllBytesAsync(Path.Combine(adminRoot, "dist/ADMIN/browser/index.html"), ct));
        var merchantIndexSha256 = P0611Tests.Hash(await File.ReadAllBytesAsync(Path.Combine(merchantRoot, "dist/INVENTORY/browser/index.html"), ct));
        await File.WriteAllTextAsync(
            Path.Combine(evidence, $"browser-postgres-{label}.json"),
            JsonSerializer.Serialize(
                new
                {
                    boundary = "Shipping admin and merchant web builds, real APIs, real PostgreSQL, local simulator and fixtures only. No live delivery or provider verification.",
                    caseName = grep,
                    accountA = seeded.PackageA.Account,
                    accountB = seeded.PackageB.Account,
                    paidTermA = seeded.PaidTermA,
                    concurrentInserted = seeded.ConcurrentInserted,
                    freshDiScopeReplayInserted = seeded.FreshScopeReplayInserted,
                    dueCountsAtFeb22 = new { before = seeded.PaidBefore, equal = seeded.PaidEqual, after = seeded.PaidAfter },
                    trialThresholds = seeded.TrialDue.Select(item => new { item.Threshold, item.DueAt }),
                    jobCountBeforeBrowser = seeded.ExpectedJobIds.Length,
                    jobCountAfterBrowser = finalJobCount,
                    contactJobsPersisted = (await P0701Tests.ReadJobsForRecipientAsync(
                        paid.Hosts.LicensingOwner,
                        seeded.PackageA.Account,
                        LicenseNoticesV1.ScopeAccount,
                        Licensing.Application.Notifications.NotificationPlanner.EmailRecipientKey(seeded.Contact.Email))).Count,
                    adminIndexSha256,
                    merchantIndexSha256,
                },
                Indented),
            ct);
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

    private static async Task<long> CountDueAsync(NpgsqlConnectionStringBuilder settings, Guid accountId, DateTimeOffset at)
    {
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM public.notification_jobs WHERE account_id = $1 AND due_at <= $2", conn);
        cmd.Parameters.AddWithValue(accountId);
        cmd.Parameters.AddWithValue(at);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<Guid[]> ReadAccountJobIdsAsync(NpgsqlConnectionStringBuilder settings, Guid accountId, CancellationToken ct)
    {
        var list = new List<Guid>();
        await using var conn = new NpgsqlConnection(settings.ConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT id FROM public.notification_jobs WHERE account_id = $1 ORDER BY due_at ASC, id ASC", conn);
        cmd.Parameters.AddWithValue(accountId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(reader.GetGuid(0));
        return [.. list];
    }

    private sealed record BrowserAdmins(string BillingSeed, string SupportSeed, string SecuritySeed);

    private sealed record SeededBrowserData(
        P0308Fixture.Package PackageA,
        P0308Fixture.Package PackageB,
        Guid PaidTermA,
        Merchant Staff,
        Merchant BillingManager,
        Merchant Contact,
        int ConcurrentInserted,
        int FreshScopeReplayInserted,
        long PaidBefore,
        long PaidEqual,
        long PaidAfter,
        (string Threshold, DateTimeOffset DueAt)[] TrialDue,
        Guid[] ExpectedJobIds);
}

internal sealed class P0701BrowserFactAttribute : FactAttribute
{
    public P0701BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0701_RUN_BROWSER") != "1")
            Skip = "Set P0701_RUN_BROWSER=1 after building both shipping web apps.";
    }
}
