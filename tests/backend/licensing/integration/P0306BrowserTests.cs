using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0306BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Config = "../../../tests/backend/licensing/browser/playwright.p0306.config.ts";

    [P0306BrowserFact]
    [Trait("Category", "Browser")]
    public async Task FinanceConfirmsFullManualReceiptAndRecoversLostReply()
    {
        var evidence = Environment.GetEnvironmentVariable("P0306_EVIDENCE_DIR");
        Assert.False(string.IsNullOrWhiteSpace(evidence));
        Directory.CreateDirectory(evidence);
        var temporary = Directory.CreateTempSubdirectory("intelibill-p0306-");
        try
        {
            await VerifyBrowserAsync(evidence, temporary.FullName);
        }
        finally
        {
            temporary.Delete(recursive: true);
        }
    }

    private async Task VerifyBrowserAsync(string evidence, string temporary)
    {
        var clock = new P0201Clock { Now = P0302Fixture.WholeSecondStart };
        await using var fixture = await P0302Fixture.StartAsync(database, clock, P0306Tests.Settings, browser: true);
        var purchase = await fixture.AcceptedQuoteAsync(names: ["Manual Central", "Manual East", "Manual North"]);
        var seed = await P0306Tests.EnrollBillingAsync(fixture, P0306Tests.BillingEmail);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var ct = timeout.Token;
        var root = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
        await using var web = await StartAdminWebAsync(root, fixture.Delivery.LicensingApi.BaseAddress!, ct);
        await SetFixtureBrowserOriginAsync(fixture, web.Origin, ct);
        var secrets = JsonSerializer.SerializeToUtf8Bytes(new
        {
            email = P0306Tests.BillingEmail, password = P0114Tests.InviteePassword, seed,
        });
        var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using (var file = new FileStream(Path.Combine(temporary, "admin.json"), fileOptions))
            await file.WriteAsync(secrets, ct);

        var start = StartInfo(root, web.Origin, evidence, temporary, purchase);
        await using var browser = new OwnedProcess(start);
        var stdout = browser.Process.StandardOutput.ReadToEndAsync(ct);
        var stderr = browser.Process.StandardError.ReadToEndAsync(ct);
        await CoordinateAsync(browser.Process, fixture, purchase, temporary, ct);
        await browser.Process.WaitForExitAsync(ct);
        var report = await stdout;
        output.WriteLine(report);
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"),
            $"bun x playwright test --config {Config}\n{report}\n{await stderr}\nexit={browser.Process.ExitCode}\n", ct);
        Assert.True(browser.Process.ExitCode == 0 && report.Contains("1 passed", StringComparison.Ordinal) &&
            !report.Contains("skipped", StringComparison.OrdinalIgnoreCase), report + await stderr);
        await RecordAsync(fixture, purchase, root, evidence, ct);
    }

    internal static async Task SetFixtureBrowserOriginAsync(P0302Fixture fixture, string origin, CancellationToken ct)
    {
        // Bind the disposable fixture's enrolled identities to the origin allocated by the shipping server.
        await using var connection = new NpgsqlConnection(fixture.Hosts.LicensingOwner.ConnectionString);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        const string table = "public.initial_admin_bootstrap";
        const string trigger = "initial_admin_bootstrap_terminal";
        await using var suspend = new NpgsqlCommand($"ALTER TABLE {table} DISABLE TRIGGER {trigger}", connection, transaction);
        await suspend.ExecuteNonQueryAsync(ct);
        await using var update = new NpgsqlCommand($"UPDATE {table} SET admin_origin = $1 WHERE consumed_at IS NOT NULL", connection, transaction);
        update.Parameters.AddWithValue(origin);
        Assert.Equal(1, await update.ExecuteNonQueryAsync(ct));
        await using var restore = new NpgsqlCommand($"ALTER TABLE {table} ENABLE TRIGGER {trigger}", connection, transaction);
        await restore.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static ProcessStartInfo StartInfo(string root, string origin, string evidence, string temporary,
        P0302Fixture.Purchase purchase)
    {
        var start = new ProcessStartInfo("bun") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", Config })
            start.ArgumentList.Add(arg);
        start.Environment["NODE_PATH"] = Path.Combine(root, "node_modules");
        start.Environment["P0112_ADMIN_WEB"] = origin;
        start.Environment["P0306_EVIDENCE_DIR"] = evidence;
        start.Environment["P0306_TEMPORARY"] = temporary;
        start.Environment["P0306_ACCOUNT"] = purchase.Account.ToString("D");
        start.Environment["P0306_ORDER"] = purchase.Quote.OrderId.ToString("D");
        start.Environment["P0306_AMOUNT"] = purchase.Quote.AmountPaise.ToString(CultureInfo.InvariantCulture);
        return start;
    }

    private static async Task CoordinateAsync(Process process, P0302Fixture fixture, P0302Fixture.Purchase purchase, string temporary, CancellationToken ct)
    {
        var auth = fixture.Hosts.Fixture.Clock;
        var normalFixtureTime = auth.Now;
        auth.Now = normalFixtureTime.AddMinutes(-5);
        async Task SignalAsync(string marker) => await File.WriteAllTextAsync(Path.Combine(temporary, marker),
            auth.Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ct);

        await SignalAsync("clock-initial");
        if (!await AwaitMarkerAsync(process, temporary, "age", ct)) return;
        auth.Now = normalFixtureTime;
        await SignalAsync("clock-aged");
        if (!await AwaitMarkerAsync(process, temporary, "applying", ct)) return;
        await fixture.Delivery.DrainAsync(source: true);
        await fixture.Delivery.DrainAsync(source: false);
        var paidOrder = await P0302Fixture.StatusAsync(purchase.Payer.Client, purchase.Quote);
        var acknowledged = await CountAcknowledgedAsync(fixture, purchase.Quote.OrderId);
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("P0306_EVIDENCE_DIR")!, "delivery-proof.json"),
            JsonSerializer.Serialize(new { acknowledged, payment = paidOrder.GetProperty("payment").GetString(),
                fulfilment = paidOrder.GetProperty("fulfilment").GetString(), access = paidOrder.GetProperty("access").GetString() }), ct);
        Assert.Equal(3L, acknowledged);
        P0302Fixture.AssertStatus(paidOrder, "Verified", "Fulfilled", "Applied");
        await SignalAsync("delivered");
    }

    private static async Task<bool> AwaitMarkerAsync(Process process, string directory, string marker, CancellationToken ct)
    {
        while (!process.HasExited && !File.Exists(Path.Combine(directory, marker)))
            await Task.Delay(50, ct);
        return !process.HasExited;
    }

    private static async Task RecordAsync(P0302Fixture fixture, P0302Fixture.Purchase purchase, string root, string evidence,
        CancellationToken ct)
    {
        var where = $"order_id = '{purchase.Quote.OrderId:D}'";
        var receipts = await fixture.CountAsync("manual_payment_receipts", where);
        var attempts = await fixture.CountAsync("payment_attempts", where);
        var fulfilments = await fixture.CountAsync("payment_fulfilments", where + " AND manual_receipt_id IS NOT NULL AND attempt_id IS NULL");
        var terms = await fixture.CountAsync("subscription_term_facts", $"""
            basis = 'Paid' AND term_id IN (SELECT term_id FROM public.payment_fulfilments WHERE {where})
            """);
        var shops = await fixture.CountAsync("payment_fulfilment_shops", where);
        var deliveries = await fixture.CountAsync("payment_fulfilment_deliveries", where);
        var acknowledged = await CountAcknowledgedAsync(fixture, purchase.Quote.OrderId);
        Assert.Equal((1L, 0L, 1L, 1L, 3L, 3L, 3L), (receipts, attempts, fulfilments, terms, shops, deliveries, acknowledged));
        P0302Fixture.AssertStatus(await P0302Fixture.StatusAsync(purchase.Payer.Client, purchase.Quote), "Verified", "Fulfilled", "Applied");
        var index = await File.ReadAllBytesAsync(Path.Combine(root, "dist/ADMIN/browser/index.html"), ct);
        var git = new ProcessStartInfo("git") { WorkingDirectory = ProjectPaths.Root, RedirectStandardOutput = true };
        git.ArgumentList.Add("rev-parse");
        git.ArgumentList.Add("HEAD");
        await using var head = new OwnedProcess(git);
        var sha = (await head.Process.StandardOutput.ReadToEndAsync(ct)).Trim();
        await head.Process.WaitForExitAsync(ct);
        await File.WriteAllTextAsync(Path.Combine(evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            sha, adminIndexSha256 = Convert.ToHexStringLower(SHA256.HashData(index)), receipts, attempts,
            fulfilments, terms, shops, deliveries, acknowledged, applied = true, command = "bun x playwright test --config " + Config,
            passed = 1, skipped = 0, scope = "Local fixture bank, live Billing/TOTP, licensing and merchant APIs, real PostgreSQL.",
        }), ct);
    }
    private static async Task<long> CountAcknowledgedAsync(P0302Fixture fixture, Guid order) =>
        (long)(await LicensingPostgreSqlFixture.ReadScalarAsync(fixture.Hosts.LicensingOwner, $"""
            SELECT count(*) FROM public.payment_fulfilment_deliveries d
            JOIN public.entitlement_snapshots s USING (account_id, shop_id, shop_revision)
            JOIN public.entitlement_outbox o USING (event_id)
            WHERE d.order_id = '{order:D}' AND o.status = 'Applied'
            """))!;

}

internal sealed class P0306BrowserFactAttribute : FactAttribute
{
    public P0306BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0306_RUN_BROWSER") != "1")
            Skip = "Set P0306_RUN_BROWSER=1 after building the shipping administrator application.";
    }
}
