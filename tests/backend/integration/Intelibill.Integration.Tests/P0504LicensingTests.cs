using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Intelibill.Domain.Enums;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace Intelibill.Integration.Tests;

[Collection("Integration Tests")]
public sealed partial class P0504LicensingTests(PostgreSqlTestFixture fixture, ITestOutputHelper output)
    : OfflinePermitHttpFixture(fixture, output)
{
    [Theory]
    [InlineData("valid", null)]
    [InlineData("start", null)]
    [InlineData("before", "OfflineLicenceHistory.OutsidePermitWindow")]
    [InlineData("deadline", "OfflineLicenceHistory.OutsidePermitWindow")]
    [InlineData("unsupported", "OfflineLicenceHistory.EvidenceUnsupported")]
    [InlineData("shape", "OfflineLicenceHistory.EvidenceUnsupported")]
    [InlineData("digest", "OfflineLicenceHistory.EvidenceInvalid")]
    [InlineData("signature", "OfflineLicenceHistory.EvidenceInvalid")]
    [InlineData("key", "OfflineLicenceHistory.EvidenceInvalid")]
    [InlineData("lease", "OfflineLicenceHistory.EvidenceInvalid")]
    [InlineData("missing", "OfflineLicenceHistory.EvidenceMissing")]
    [InlineData("restricted", "OfflineLicenceHistory.RestrictionConflict")]
    [InlineData("beforeRestriction", null)]
    [InlineData("issuer", "OfflineReconciliation.IssuerNotSubmitter")]
    [InlineData("arithmetic", "Sale.OfflineArithmeticMismatch")]
    [InlineData("expiry", "Expiry.ReviewRequired")]
    public async Task Historical_sync_preserves_first_receipt_and_checks_local_permit(string scenario, string? code)
    {
        var start = At("2027-01-20T12:00:00Z");
        var end = start.AddHours(1);
        using var host = CreateHost();
        await host.InitializeAsync();
        using var client = CreateClient(host);
        var actor = await OpenHistoricalShopAsync(client, start, end);
        var sale = await IssuedInvoiceAsync(client, actor, start, end, scenario);
        if (scenario is "restricted" or "beforeRestriction")
            await ApplyAsync(actor.Binding, end.AddDays(1), revision: 2, restriction: "Trading",
                suspensionEffectiveAt: scenario == "restricted" ? start : end);
        _clock.Now = end.AddMinutes(5);
        if (scenario == "valid")
            await AssertProblemAsync(await client.PostAsJsonAsync("/api/sales/invoice-leases/reserve", new { deviceId = Device }, Ct),
                System.Net.HttpStatusCode.Forbidden, "Licence.Expired");
        var result = await SyncAsync(client, sale);
        await using var db = Open(Migrator);
        var envelope = await db.OfflineSubmissions.SingleAsync(x => x.ShopId == actor.Shop, Ct);
        Assert.Equal(_clock.Now, envelope.ReceivedAt);
        var expected = sale.Deserialize<OfflineSaleSyncCommand>(JsonSerializerOptions.Web);
        var expectedPayload = JsonSerializer.SerializeToNode(expected, JsonSerializerOptions.Web);
        Assert.True(JsonNode.DeepEquals(expectedPayload, JsonNode.Parse(envelope.PayloadJson)), "Persisted invoice differs from the frozen request.");

        _output.WriteLine("scenario={0} issued={1:O} invoice={2} hash={3} status={4}", scenario,
            envelope.ClaimedSoldAt, envelope.InvoiceNumber, envelope.PayloadHash, result.GetProperty("status"));
        if (code is null)
        {
            Assert.Equal("created", result.GetProperty("status").GetString());
            Assert.NotNull(envelope.LicencePermitId);
            Assert.NotNull(envelope.LicenceGrantId);
            var posted = await db.Sales.SingleAsync(x => x.ShopId == actor.Shop, Ct);
            Assert.Equal((sale["soldAt"]!.GetValue<DateTimeOffset>(), sale["invoiceNumber"]!.GetValue<string>(), envelope.PayloadHash),
                (posted.SoldAt, posted.InvoiceNumber, posted.RequestHash));
            if (scenario == "valid")
            {
                await VerifyReplayAsync(client, actor, sale, end);
                var first = await db.OfflineSubmissions.AsNoTracking().SingleAsync(row => row.Id == envelope.Id, Ct);
                Assert.True(envelope.PayloadJson == first.PayloadJson && envelope.PayloadHash == first.PayloadHash &&
                    envelope.ReceivedAt == first.ReceivedAt, "Replay changed the original receipt.");
            }
        }
        else
        {
            Assert.Equal("NeedsReview", result.GetProperty("status").GetString());
            Assert.Equal(code, result.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.False(await db.Sales.AnyAsync(x => x.ShopId == actor.Shop, Ct));
            Assert.Equal(result.GetProperty("reviewCaseId").GetGuid(), (await SyncAsync(client, sale)).GetProperty("reviewCaseId").GetGuid());
        }
    }

    private async Task<Actor> OpenHistoricalShopAsync(HttpClient client, DateTimeOffset start, DateTimeOffset end)
    {
        var actor = await OpenShopAsync(client, start, end);
        // Shop creation seeds expiry history using wall time. Give this fixed-date fixture a known
        // policy before issuance, then obtain the batch's real expiry proof through the snapshot.
        await using var db = Open(Migrator);
        await LegacyOfflineExpiryHistory.SeedAsync(db, actor.Shop);
        await EnforceAsync(actor.Shop);
        return actor;
    }

    private static async Task<JsonElement> SyncAsync(HttpClient client, JsonNode sale)
    {
        using var response = await client.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = Device, sales = new[] { sale } }, Ct);
        return (await ReadSuccessAsync(response)).GetProperty("results")[0];
    }

    private async Task VerifyReplayAsync(HttpClient client, Actor actor, JsonNode sale, DateTimeOffset end)
    {
        Assert.Equal("duplicate", (await SyncAsync(client, sale)).GetProperty("status").GetString());
        var changed = sale.DeepClone();
        changed["licenceEvidence"]!["keyId"] = "unknown";
        var conflict = await SyncAsync(client, changed);
        Assert.Equal("Sale.IdempotencyConflict", conflict.GetProperty("errors")[0].GetProperty("code").GetString());
        await ApplyAsync(actor.Binding, end.AddDays(1), revision: 2, restriction: "Security");
        using var denied = await client.PostAsJsonAsync("/api/sales/offline-sync", new { deviceId = Device, sales = new[] { sale } }, Ct);
        await AssertProblemAsync(denied, System.Net.HttpStatusCode.Forbidden, "Licence.SecuritySuspended");
    }

    private static async Task<JsonNode> IssuedInvoiceAsync(HttpClient client, Actor actor, DateTimeOffset start, DateTimeOffset end, string scenario, DateTimeOffset? soldAtOverride = null)
    {
        var lease = await ReserveHttpAsync(client);
        var grant = RequireLicenceEvidence(lease);
        var stream = await StreamSnapshotHttpAsync(client, BoundStreamUrl(lease.GetProperty("leaseId").GetGuid(),
            grant.GetProperty("grantId").GetGuid(), Device));
        var delivery = stream.RequirePermitImmediatelyBeforeComplete();
        Assert.Equal(end, delivery.GetProperty("validUntil").GetDateTimeOffset());
        var evidence = JsonSerializer.SerializeToNode(new
        {
            version = 1, permitId = delivery.GetProperty("permitId"), grantId = grant.GetProperty("grantId"),
            grantSha256 = grant.GetProperty("grantSha256"), leaseId = lease.GetProperty("leaseId"),
            snapshotId = stream.Metadata.GetProperty("snapshotId"), keyId = delivery.GetProperty("keyId"),
            signedPermit = delivery.GetProperty("signedPermit"),
        })!;
        var soldAt = soldAtOverride ?? scenario switch { "start" => start, "before" => start.AddMicroseconds(-1), "deadline" => end, _ => end.AddMicroseconds(-1) };
        var number = lease.GetProperty("prefix").GetString() + lease.GetProperty("rangeStart").GetInt32().ToString("D6", CultureInfo.InvariantCulture);
        var sale = Invoice(number, actor.User, soldAt, stream);
        switch (scenario)
        {
            case "digest": evidence["grantSha256"] = new string('0', 64); break;
            case "signature": evidence["signedPermit"] = "invalid-local-fixture"; break;
            case "key": evidence["keyId"] = "unknown"; break;
            case "lease": evidence["leaseId"] = Guid.NewGuid(); break;
            case "unsupported": evidence["version"] = 2; break;
            case "issuer": sale["issuedByUserId"] = Guid.NewGuid(); break;
            case "expiry": sale["items"]![0]!["expiryEvidence"]!["policyVersion"] = 77; break;
            case "arithmetic": sale["items"]![0]!["taxableAmount"] = 100; break;
        }
        if (scenario != "missing") sale["licenceEvidence"] = scenario == "shape" ? new JsonArray(1) : evidence;
        return sale;
    }

    private static JsonNode Invoice(string number, Guid user, DateTimeOffset soldAt, SnapshotStreamResponse stream)
    {
        var batch = stream.Records.Single(x => x.GetProperty("type").GetString() == "batch").GetProperty("batch");
        return JsonSerializer.SerializeToNode(new
        {
            clientSaleId = Guid.NewGuid().ToString("N"), invoiceNumber = number, soldAt, issuedByUserId = user,
            paymentMethod = (int)PaymentMethod.Cash, paidAmount = 2, subtotalBeforeDiscount = 2,
            totalBeforeDiscount = 2, totalAmount = 2, items = new[] { new
            {
                barcode = batch.GetProperty("barcode"), batchNumber = "LOT", itemName = "Fixture stock", quantity = 1,
                costPrice = 1, salesPrice = 2, mrp = 2, inventoryBatchId = batch.GetProperty("batchId"),
                preTaxAmountBeforeDiscount = 2, taxableAmount = 2, totalAmount = 2,
                expiryEvidence = new { evidenceId = batch.GetProperty("expiryEvidenceId"),
                    catalogVersion = stream.Metadata.GetProperty("catalogVersion"),
                    policyVersion = batch.GetProperty("effectivePolicy").GetProperty("version"),
                    businessDate = soldAt.ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), decisionCode = "Allowed" },
            } },
        })!;
    }
}
