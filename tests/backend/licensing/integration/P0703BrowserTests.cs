using System.Diagnostics;
using System.Text.Json;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0308Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0703BrowserTests(LicensingPostgreSqlFixture database, MailpitFixture mailpit)
    : IClassFixture<LicensingPostgreSqlFixture>, IClassFixture<MailpitFixture>
{
    private sealed record Operator(string Email, string Seed);
    private sealed record Queued(Package Target, Guid Term, Guid Job, Guid Delivery, Package Control, Guid ControlDelivery);

    [P0306BrowserFact]
    [Trait("Category", "Browser")]
    public async Task OperatorSeesWhyAReminderWasSuppressed()
    {
        var evidence = Environment.GetEnvironmentVariable("P0703_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence));
        var scratch = Directory.CreateTempSubdirectory("p0703-admin-");
        using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try
        {
            await RunAsync(scratch.FullName, evidence, limit.Token);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private async Task RunAsync(string scratch, string evidence, CancellationToken ct)
    {
        Directory.CreateDirectory(evidence);
        var root = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
        await using var paid = await P0308Fixture.StartAsync(database, P0703Tests.Start, P0703Tests.Settings(mailpit), browser: true);
        var queued = await SeedAsync(paid);
        await WriteCredentialsAsync(paid, queued, scratch, ct);
        await using var relay = await P0310ApiRelay.StartAsync(paid.Checkout.Delivery.LicensingApi.BaseAddress!, ct);
        await using var web = await StartAdminWebAsync(root, relay.Origin, ct);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(paid.Checkout, web.Origin, ct);
        await using var browser = new OwnedProcess(BrowserStart(root, web.Origin, scratch, evidence));
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await ProcessRenewalSignalAsync(paid, queued, browser.Process, scratch, ct);
        await browser.Process.WaitForExitAsync(ct);
        var log = await stdout + await stderr;
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), log, ct);
        Assert.True(browser.Process.ExitCode == 0 && log.Contains("1 passed") && !log.Contains("skipped"), log);
        await WriteEvidenceAsync(paid, queued, evidence, ct);
    }

    private static async Task<Queued> SeedAsync(P0308Fixture paid)
    {
        var target = await paid.PackageAsync(1);
        var control = await paid.PackageAsync(1);
        var (_, term) = await PurchaseAsync(target, "Month");
        await PurchaseAsync(control, "Month");
        await P0608Tests.DrainAsync(paid);
        await P0701Tests.RunPlannerPassAsync(paid.Hosts.Licensing.Services);
        paid.At(P0703Tests.Due);
        var job = await JobAsync(paid, target);
        var controlJob = await JobAsync(paid, control);
        return new(target, term, job, await DeliveryAsync(paid, job), control, await DeliveryAsync(paid, controlJob));
    }

    private static async Task<Guid> JobAsync(P0308Fixture paid, Package package) =>
        (Guid)(await paid.ScalarAsync($"""
            SELECT id FROM notification_jobs WHERE scope_kind = 'Account' AND account_id = '{package.Account:D}'
                AND recipient_user_id = '{package.Payer.UserId:D}' AND threshold = 'PaidMinus7Days'
            """))!;

    private static async Task<Guid> DeliveryAsync(P0308Fixture paid, Guid job) =>
        (Guid)(await paid.ScalarAsync($"SELECT id FROM notification_email_deliveries WHERE job_id = '{job:D}' AND state = 'Pending'"))!;

    private static async Task WriteCredentialsAsync(P0308Fixture paid, Queued queued, string scratch, CancellationToken ct)
    {
        var catalogue = paid.Checkout.Trial.Catalogue;
        using var owner = await catalogue.SignInOwnerAsync();
        var operators = new Dictionary<string, Operator>();
        foreach (var role in new[] { "billing", "support", "licensing-manager", "security-administrator" })
        {
            var email = $"p0703-{role}@example.test";
            var seed = await P0611Tests.EnrollAsync(paid.Checkout, owner, email, role);
            operators[role] = new(email, seed);
        }
        catalogue.Clock.Advance(TimeSpan.FromSeconds(30));
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, "fixture.json"), options);
        await JsonSerializer.SerializeAsync(file, new
        {
            operators, password = P0114Tests.InviteePassword, step = paid.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds() / 30,
            account = queued.Target.Account, job = queued.Job,
        }, cancellationToken: ct);
    }

    private static ProcessStartInfo BrowserStart(string root, string origin, string scratch, string evidence)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Arguments = "x playwright test --config ../../../tests/backend/licensing/browser/playwright.p0703.config.ts";
        start.Environment["P0703_ADMIN_WEB"] = origin;
        start.Environment["P0703_TEMPORARY"] = scratch;
        start.Environment["P0703_EVIDENCE_DIR"] = evidence;
        start.Environment["NODE_PATH"] = Path.Combine(root, "node_modules");
        return start;
    }

    private static async Task ProcessRenewalSignalAsync(
        P0308Fixture paid,
        Queued queued,
        Process process,
        string scratch,
        CancellationToken ct)
    {
        while (!process.HasExited && !File.Exists(Path.Combine(scratch, "renew.req")))
            await Task.Delay(30, ct);
        if (process.HasExited)
            return;
        var (_, status) = await RenewAsync(queued.Target, queued.Term, "Month");
        P0302Fixture.AssertStatus(status, "Verified", "Fulfilled", "Applying");
        await P0703Tests.DrainMailAsync(paid);
        await File.WriteAllTextAsync(Path.Combine(scratch, "renew.ack"), "drained", ct);
    }

    private async Task WriteEvidenceAsync(P0308Fixture paid, Queued queued, string evidence, CancellationToken ct)
    {
        var stale = await mailpit.MessagesAsync(queued.Target.Payer.Email);
        Assert.Empty(stale);
        var control = Assert.Single(await mailpit.MessagesAsync(queued.Control.Payer.Email));
        var messageId = control.GetProperty("MessageID").GetString()?.Trim('<', '>');
        Assert.Equal($"{queued.ControlDelivery:N}@notices.intelibill.invalid", messageId);
        var events = await paid.ScalarAsync($"""
            SELECT string_agg(state || '/' || reason, ',' ORDER BY recorded_at, state)
            FROM notification_email_events WHERE delivery_id = '{queued.Delivery:D}'
            """);
        Assert.Equal("Leased/Leased,Suppressed/SourceNoLongerCurrent", events);
        var observation = new
        {
            job = queued.Job, delivery = queued.Delivery, decisionAt = P0703Tests.Due, events,
            staleCaptures = stale.Length, controlCaptures = 1, controlDelivery = queued.ControlDelivery,
            browser = "Chromium, 1 passed, zero skips", languages = new[] { "en-IN", "hi-IN" },
            billing = "Localized reason after refresh", licensingManager = "SourceNoLongerCurrent",
            support = "Redacted", security = 403, viewport = 390, transport = "Local Mailpit and simulator",
        };
        await File.WriteAllTextAsync(Path.Combine(evidence, "p0703-browser.json"), JsonSerializer.Serialize(observation), ct);
    }
}
