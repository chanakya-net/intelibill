using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;
using static Intelibill.Licensing.Integration.Tests.P0209Fixture;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0509Tests
{
    private static readonly TimeSpan PinnedBuildTimeout = TimeSpan.FromMinutes(10);
    private static readonly object PinnedBuildGate = new();
    private static PinnedPermitMaterial? SharedPinnedBuild;

    internal static string EvidenceDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("P0509_EVIDENCE_DIR");
        var root = string.IsNullOrWhiteSpace(configured) ? Path.GetTempPath() : configured;
        Directory.CreateDirectory(root);
        return root;
    }

    private string CreateScenarioEvidence(string name)
    {
        var dir = Path.Combine(EvidenceDirectory(), $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        output.WriteLine("P05-09 {0} evidence: {1}", name, dir);
        return dir;
    }

    private async Task AssertSingleBrowserPassAsync(Process browser, Task<string> browserReport)
    {
        var report = await browserReport;
        output.WriteLine(report);
        Assert.True(
            browser.ExitCode == 0
            && report.Contains("1 passed", StringComparison.Ordinal)
            && !report.Contains("skipped", StringComparison.Ordinal),
            report);
    }

    private async Task WriteCorrelatedEvidenceAsync(string dir, string file, object summary, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(summary, IndentedJson);
        await File.WriteAllTextAsync(Path.Combine(dir, file), json, ct);
        await File.WriteAllTextAsync(Path.Combine(EvidenceDirectory(), file), json, ct);
        output.WriteLine("P05-09 correlated {0}: {1}", file, JsonSerializer.Serialize(summary));
    }

    private static PinnedPermitMaterial EnsurePinnedBrowserBuild(string frontend)
    {
        lock (PinnedBuildGate)
        {
            if (SharedPinnedBuild is not null && Directory.Exists(SharedPinnedBuild.BrowserRoot))
                return SharedPinnedBuild;

            using var rsa = TestRsaKeys.Create(TestRsaKeyPurpose.General);
            var privatePem = rsa.ExportPkcs8PrivateKeyPem();
            var publicSpki = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
            var pinMap = JsonSerializer.Serialize(new Dictionary<string, string> { [BrowserPermitKeyId] = publicSpki });

            RunPinnedBuild(frontend, pinMap);

            var browserRoot = Path.Combine(frontend, ".ui-audit", "offline-permit-build", "browser");
            Assert.True(File.Exists(Path.Combine(browserRoot, "ngsw-worker.js")), "Pinned browser build missing ngsw-worker.js.");
            SharedPinnedBuild = new(BrowserPermitKeyId, privatePem, publicSpki, browserRoot);
            return SharedPinnedBuild;
        }
    }

    /// <summary>
    /// Builds the pinned merchant bundle in a child process. Both pipes drain concurrently so a failing build that
    /// fills stderr cannot block the child, and the wait is bounded so a hung build fails the test instead of hanging.
    /// </summary>
    private static void RunPinnedBuild(string frontend, string pinMap)
    {
        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = frontend, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("-e");
        start.ArgumentList.Add(
            "import { buildPinnedApp } from './tests/ui-audit/release/offline-permit-finalization-fixture.ts';" +
            "buildPinnedApp(JSON.parse(process.env.P0509_PIN_MAP!));");
        start.Environment["P0509_PIN_MAP"] = pinMap;
        using var proc = Process.Start(start) ?? throw new InvalidOperationException("Failed to start pinned build.");
        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();
        if (!proc.WaitForExit(PinnedBuildTimeout))
        {
            proc.Kill(entireProcessTree: true);
            Assert.Fail($"Pinned merchant build did not finish within {PinnedBuildTimeout}.");
        }
        Assert.True(Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(30)), "Pinned merchant build output did not close.");
        Assert.True(proc.ExitCode == 0, $"Pinned merchant build failed:\n{stdout.Result}\n{stderr.Result}");
    }

    private static MerchantWebBuild StagePinnedMerchantWeb(string frontend, PinnedPermitMaterial pinned)
    {
        var staged = new MerchantWebBuild(frontend);
        try
        {
            var browserTarget = Path.Combine(staged.Root, "dist/INVENTORY/browser");
            if (Directory.Exists(browserTarget)) Directory.Delete(browserTarget, recursive: true);
            foreach (var file in Directory.EnumerateFiles(pinned.BrowserRoot, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(browserTarget, Path.GetRelativePath(pinned.BrowserRoot, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
            return staged;
        }
        catch { staged.Dispose(); throw; }
    }

    private static Dictionary<string, string?> PermitSigningSettings(PinnedPermitMaterial pinned) => new()
    {
        ["OfflineLicensePermit:LocalKeyId"] = pinned.KeyId,
        ["OfflineLicensePermit:LocalPrivateKeyPem"] = pinned.PrivateKeyPem,
    };

    private static async Task<(Merchant Shop, Guid Account, string Barcode)> PrepareTemporaryShopAsync(
        P0213Fixture fixture, DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        var shop = await CreateTradingShopAsync(fixture.Hosts);
        var account = await CreateAccountAsync(shop.Client);
        var anchor = startsAt.ToOffset(TimeSpan.FromHours(5.5));
        var term = await SeedTermAsync(fixture.Hosts, account, new TermSeed(
            startsAt, endsAt, Basis: "Temporary", PricePaise: null,
            AnchorLocal: FormattableString.Invariant($"{anchor:yyyy-MM-dd HH:mm:ss}")));
        await P0213Fixture.LinkAsync(account, shop, shop, term);
        await DrainBothAsync(fixture);
        await P0403Tests.EnrollAsync(fixture, shop);
        return (shop, account, await SeedGoodsStockAsync(shop));
    }

    private static async Task<(Merchant Shop, Guid Account, string Barcode)> PreparePaidShopAsync(
        P0213Fixture fixture, DateTimeOffset at)
    {
        var (shop, account) = await P0507Tests.Run.OpenShopAsync(fixture, at);
        await DrainBothAsync(fixture);
        await P0403Tests.EnrollAsync(fixture, shop);
        return (shop, account, await SeedGoodsStockAsync(shop));
    }

    private static async Task<string> SeedGoodsStockAsync(Merchant shop, string? label = null)
    {
        var barcode = "P0509" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var itemName = label is null ? GoodsName : $"{GoodsName} {label}";
        var batchNumber = label is null ? "LOT-P0509" : $"LOT-{label}";
        using var response = await shop.Client.PostAsJsonAsync("/api/inventory/inbound", new
        {
            itemName, barcode, uom = "pcs", batchNumber,
            quantity = 10m, totalPurchaseCost = 400m, mrp = 60m, salesPrice = 50m,
            taxRatePercent = 0m, taxIncluded = false, purchaseTaxIncluded = false,
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return barcode;
    }

    private static async Task DrainBothAsync(P0213Fixture fixture)
    {
        AdvanceDeliveryLeaseClock(fixture);
        await fixture.DrainAsync(source: true);
        AdvanceDeliveryLeaseClock(fixture);
        await fixture.DrainAsync(source: false);
    }

    private static void AdvanceDeliveryLeaseClock(P0213Fixture fixture)
    {
        var latest = fixture.Hosts.Fixture.Clock.Now > fixture.Hosts.StatusClock.Now
            ? fixture.Hosts.Fixture.Clock.Now : fixture.Hosts.StatusClock.Now;
        if (fixture.Transport.LeaseClock.Now <= latest)
            fixture.Transport.LeaseClock.Now = latest.AddSeconds(1);
    }

    private static OwnedProcess StartReleaseBrowser(
        string frontend, string evidence, string origin, string email, string barcode, string scenario, PinnedPermitMaterial pinned)
    {
        var start = new ProcessStartInfo("bun")
        {
            WorkingDirectory = frontend, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "x", "playwright", "test", "--config", "tests/ui-audit/release/playwright.release.config.ts", "licensing-offline.spec.ts" })
            start.ArgumentList.Add(arg);
        start.Environment["P0509_LIVE_SCENARIO"] = scenario;
        start.Environment["P0509_ORIGIN"] = origin;
        start.Environment["P0509_EMAIL"] = email;
        start.Environment["P0509_PASSWORD"] = P0201Tests.Password;
        start.Environment["P0509_BARCODE"] = barcode;
        start.Environment["P0509_EVIDENCE"] = evidence;
        start.Environment["P0509_KEY_ID"] = pinned.KeyId;
        start.Environment["P0509_PUBLIC_SPKI"] = pinned.PublicKeySpki;
        return new OwnedProcess(start);
    }

    private static async Task<string> CaptureBrowserAsync(Process process, string evidence, CancellationToken ct)
    {
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        var report = (await stdout) + (await stderr);
        await File.WriteAllTextAsync(Path.Combine(evidence, "playwright.log"), report, ct);
        return report;
    }

    private static async Task<JsonElement> WaitForPhaseJsonAsync(
        string evidence, string phase, Process browser, Task<string> report, CancellationToken ct)
    {
        var path = Path.Combine(evidence, $"observations-{phase}.json");
        while (!File.Exists(path))
        {
            if (browser.HasExited) Assert.Fail(await report);
            await Task.Delay(50, ct);
        }
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> WaitForPhaseWithLiveClockAsync(
        P0213Fixture fixture, string evidence, string phase, Process browser, Task<string> report, CancellationToken ct)
    {
        var path = Path.Combine(evidence, $"observations-{phase}.json");
        while (!File.Exists(path))
        {
            if (browser.HasExited) Assert.Fail(await report);
            var now = DateTimeOffset.UtcNow;
            if (now > fixture.Hosts.Fixture.Clock.Now) fixture.Hosts.Fixture.Clock.Now = now;
            await Task.Delay(25, ct);
        }
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        return doc.RootElement.Clone();
    }

    private static async Task WriteSignalAsync(string evidence, string name, string content, CancellationToken ct)
    {
        var path = Path.Combine(evidence, name);
        await File.WriteAllTextAsync(path + ".tmp", content, ct);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private static async Task<JsonElement> ReadAuthorityAsync(HttpClient client, Guid shopId)
    {
        using var response = await client.GetAsync($"/api/licensing/shops/{shopId:D}/trading-authority");
        return await P0418Tests.ReadAsync(response, HttpStatusCode.OK);
    }

    private static async Task<JsonElement> SyncSingleAsync(HttpClient client, string deviceId, JsonElement sale, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId, sales = new[] { sale } }, ct);
        return Assert.Single((await P0418Tests.ReadAsync(response, HttpStatusCode.OK)).GetProperty("results").EnumerateArray());
    }

    private static async Task<T[]> RunBarrierRaceAsync<T>(Func<Task<T>> first, Func<Task<T>> second)
    {
        using var ready = new CountdownEvent(2);
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<T> RunAsync(Func<Task<T>> op) { ready.Signal(); await go.Task.WaitAsync(TimeSpan.FromSeconds(10)); return await op(); }
        var t1 = Task.Run(() => RunAsync(first));
        var t2 = Task.Run(() => RunAsync(second));
        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)), "Race participants failed to reach barrier.");
        go.SetResult();
        return await Task.WhenAll(t1, t2);
    }

    private static async Task<HttpClient> CreateSecondSessionClientAsync(P0213Fixture fixture, string email)
    {
        var client = fixture.Hosts.Merchant.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await P0201Tests.LoginAsync(client, email));
        return client;
    }

    private static async Task<JsonElement> ReadProviderDevicePermitsAsync(P0110CookieBrowser admin, Guid account, Guid? shop = null)
    {
        var route = $"/api/admin/accounts/{account:D}/device-permits" + (shop is { } id ? $"?shop={id:D}" : string.Empty);
        using var response = await admin.SendAsync(HttpMethod.Get, route);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<long> CountMerchantAsync(P0213Fixture fixture, string table, string where) =>
        (long)(await fixture.MerchantScalarAsync($"SELECT count(*) FROM public.{table} WHERE {where}"))!;

    private static async Task<long> CountDeviceObservationsAsync(P0213Fixture fixture, Guid shopId) =>
        (long)(await fixture.MerchantScalarAsync($"""
            SELECT count(*) FROM public.entitlement_messages m JOIN public.entitlement_outbox o USING (event_id)
            WHERE m.kind = 'DeviceObservation' AND m.shop_id = '{shopId:D}'
            """))!;

    private static async Task<decimal> ReadShopStockQuantityAsync(P0213Fixture fixture, Guid shopId) =>
        (decimal)(await fixture.MerchantScalarAsync($"SELECT coalesce(sum(quantity), 0) FROM public.inventory_batches WHERE shop_id = '{shopId:D}'"))!;

    private static async Task<long> ReadInvoiceSequenceSumAsync(P0213Fixture fixture, Guid shopId) =>
        (long)(await fixture.MerchantScalarAsync($"SELECT coalesce(sum(next_number), 0)::bigint FROM public.invoice_sequences WHERE shop_id = '{shopId:D}'"))!;

    private static async Task<SubmissionEvidence?> ReadSubmissionAsync(P0213Fixture fixture, Guid shopId, string clientSaleId)
    {
        await using var conn = new NpgsqlConnection(fixture.MerchantDb.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, sale_id, invoice_number, payload_hash, payload_json::text,
                   claimed_sold_at, received_at, licence_permit_id, licence_grant_id, device_id
            FROM public.offline_submissions WHERE shop_id = $1 AND client_sale_id = $2
            """, conn);
        cmd.Parameters.AddWithValue(shopId);
        cmd.Parameters.AddWithValue(clientSaleId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7), reader.IsDBNull(8) ? null : reader.GetGuid(8), reader.GetString(9));
    }

    private static async Task<SaleEvidence?> ReadSaleAsync(P0213Fixture fixture, Guid shopId, string clientSaleId)
    {
        await using var conn = new NpgsqlConnection(fixture.MerchantDb.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            SELECT id, invoice_number, request_hash, sold_at, total_amount, actor_user_id, invoice_document::text
            FROM public.sales WHERE shop_id = $1 AND client_sale_id = $2
            """, conn);
        cmd.Parameters.AddWithValue(shopId);
        cmd.Parameters.AddWithValue(clientSaleId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetDecimal(4), reader.GetGuid(5), reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    private static DateTimeOffset ParseInstant(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record PinnedPermitMaterial(string KeyId, string PrivateKeyPem, string PublicKeySpki, string BrowserRoot);
    private sealed record SubmissionEvidence(
        Guid Id, Guid? SaleId, string InvoiceNumber, string PayloadHash, string PayloadJson,
        DateTimeOffset ClaimedSoldAt, DateTimeOffset ReceivedAt, Guid? LicencePermitId, Guid? LicenceGrantId, string DeviceId);
    private sealed record SaleEvidence(
        Guid Id, string InvoiceNumber, string RequestHash, DateTimeOffset SoldAt, decimal TotalAmount,
        Guid ActorUserId, string? InvoiceDocumentJson);
}
