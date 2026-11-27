using System.Text.Json;
using Xunit.Abstractions;
using static Intelibill.Licensing.Integration.Tests.BillingBrowserHarness;

namespace Intelibill.Licensing.Integration.Tests;

public sealed class P0611BrowserTests(LicensingPostgreSqlFixture database, ITestOutputHelper output)
    : IClassFixture<LicensingPostgreSqlFixture>
{
    private const string Config = "../../../tests/backend/licensing/browser/playwright.p0611.config.ts";
    private const string SupportEmail = "documents.browser.support@example.test";

    [P0611BrowserFact]
    [Trait("Category", "Browser")]
    public async Task FinanceAttachesInvoiceThroughShippingAdminWeb()
    {
        var evidence = Environment.GetEnvironmentVariable("P0611_EVIDENCE_DIR")!;
        Directory.CreateDirectory(evidence);
        var scratch = Directory.CreateTempSubdirectory("intelibill-documents-browser-");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try
        {
            await RunAsync(scratch.FullName, evidence, deadline.Token);
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private async Task RunAsync(string scratch, string evidence, CancellationToken ct)
    {
        await using var fixture = await P0611Tests.StartAsync(database, browser: true);
        var purchase = await P0311Tests.VerifiedAsync(fixture);
        var other = await P0209Fixture.CreateAccountAsync(purchase.Payer.Client, "Unrelated account");
        var bytes = P0611Tests.Pdf();
        await File.WriteAllBytesAsync(Path.Combine(scratch, "fixture-invoice.pdf"), bytes, ct);
        await PrepareOperatorsAsync(fixture, scratch, ct);

        var root = Path.Combine(ProjectPaths.Root, "src/frontend/licensing");
        await using var relay = await P0310ApiRelay.StartAsync(fixture.Delivery.LicensingApi.BaseAddress!, ct);
        await using var admin = await StartAdminWebAsync(root, relay.Origin, ct);
        await P0306BrowserTests.SetFixtureBrowserOriginAsync(fixture, admin.Origin, ct);
        var targets = new Dictionary<string, string>
        {
            ["ADMIN_WEB"] = admin.Origin, ["TEMPORARY"] = scratch, ["EVIDENCE_DIR"] = evidence,
            ["ACCOUNT"] = purchase.Account.ToString("D"), ["ORDER"] = purchase.Quote.OrderId.ToString("D"),
            ["FOREIGN_ACCOUNT"] = other.ToString("D"),
        };
        foreach (var phase in new[] { "attach", "denied" })
        {
            output.WriteLine($"bun x playwright test --config {Config} --grep @{phase}");
            await P0609BrowserTests.RunBrowserAsync(root, targets, phase, evidence, Config, "P0611_", output, ct);
        }
        await RecordAsync(fixture, purchase, root, evidence, bytes, ct);
    }

    private static async Task PrepareOperatorsAsync(P0302Fixture fixture, string scratch, CancellationToken ct)
    {
        using var owner = await fixture.Trial.Catalogue.SignInOwnerAsync();
        var billingSeed = await P0611Tests.EnrollAsync(fixture, owner, P0306Tests.BillingEmail, "billing");
        var supportSeed = await P0611Tests.EnrollAsync(fixture, owner, SupportEmail, "support");
        var time = fixture.Hosts.Fixture.Clock.GetUtcNow().ToUnixTimeSeconds();
        await WriteCredentialAsync(scratch, "finance", P0306Tests.BillingEmail, billingSeed, time, ct);
        await WriteCredentialAsync(scratch, "support", SupportEmail, supportSeed, time, ct);
    }

    private static async Task WriteCredentialAsync(string scratch, string name, string email, string seed, long time, CancellationToken ct)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var file = new FileStream(Path.Combine(scratch, name + ".json"), options);
        await JsonSerializer.SerializeAsync(file, new { email, seed, time, password = P0114Tests.InviteePassword }, cancellationToken: ct);
    }

    private static async Task RecordAsync(P0302Fixture fixture, P0302Fixture.Purchase purchase, string root,
        string evidence, byte[] bytes, CancellationToken ct)
    {
        var sha256 = P0611Tests.Hash(bytes);
        var count = await fixture.CountAsync("billing_document_versions", $"""
            account_id = '{purchase.Account:D}' AND order_id = '{purchase.Quote.OrderId:D}' AND
            issuer_name = '{P0611Tests.Issuer}' AND issuer_reference = '{P0611Tests.Reference}' AND
            issued_on = DATE '2027-03-10' AND version_number = 1 AND content_sha256 = '{sha256}'
            """);
        Assert.Equal(1, count);
        Assert.Equal(bytes, await P0611Tests.StoredBytesAsync(fixture));
        Assert.Equal(1L, await fixture.CountAsync("payment_confirmations"));
        var index = await File.ReadAllBytesAsync(Path.Combine(root, "dist/ADMIN/browser/index.html"), ct);
        await File.WriteAllTextAsync(Path.Combine(evidence, "build-and-postgres.json"), JsonSerializer.Serialize(new
        {
            provenance = "Local simulator payment, disposable admin identities, real Chromium and PostgreSQL",
            purchase.Account, purchase.Quote.OrderId, sha256, byteLength = bytes.Length, versions = count,
            adminIndexSha256 = P0611Tests.Hash(index), passed = 2, skipped = 0,
        }), ct);
    }
}

internal sealed class P0611BrowserFactAttribute : FactAttribute
{
    public P0611BrowserFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("P0611_RUN_BROWSER") != "1")
            Skip = "Set P0611_RUN_BROWSER=1 after building the shipping administrator application.";
    }
}
