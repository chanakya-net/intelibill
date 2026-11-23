using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using System.Text.Json;
using Intelibill.Application.Features.Tills;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Unit.Tests.Features.Sales.Commands.SyncOfflineSales;

public class OfflineSaleSyncIdempotencyHasherTests
{
    private static readonly Guid ShopId = Guid.Parse("8f0d6c1e-3a52-4c0b-9a51-5b8f2f8f1e10");
    private static readonly Guid BatchId = Guid.Parse("1b7f8e3c-5d2a-4f6e-8c9b-0a1d2e3f4a5b");

    // Captured from the hasher before calculationVersion existed: legacy retries must keep replaying as duplicates.
    private const string LegacyHash = "0CB3113F33030EA8632F78C9D7FFA13E21024D8CD5F146F94E2B16EBB52C7913";

    // Captured from the hasher before issuedByUserId existed, for a version-1 payload without an issuer.
    private const string Version1WithoutIssuerHash = "B4441BDEF5E617214385DD83F50265DB10641C7CF6F04528F186AAFBD2499CC6";

    private static readonly Guid IssuerId = Guid.Parse("5a3c2e1f-7b6d-4c8e-9f0a-1b2c3d4e5f60");

    [Fact]
    public void OptionalExpiryProof_PreservesLegacyAbsenceAndBindsEverySuppliedField()
    {
        var old = Sale(null); var oldJson = OfflineSubmissionPayload.Serialize(old);
        Assert.DoesNotContain("expiryEvidence", oldJson, StringComparison.Ordinal);
        var absent = OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", old);
        Assert.Equal(LegacyHash, absent);
        var proof = new OfflineExpiryLineEvidence(Guid.NewGuid(), 3, 2, new(2026, 10, 20), new(2026, 10, 21), "Expiry.ExpiringSoon");
        var frozen = old with { Items = [old.Items[0] with { ExpiryEvidence = proof }] };
        var hash = OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", frozen);
        Assert.NotEqual(absent, hash);
        var restored = OfflineSubmissionPayload.Deserialize(OfflineSubmissionPayload.Serialize(frozen))!;
        Assert.Equal(proof, restored.Items[0].ExpiryEvidence);
        Assert.Equal(hash, OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", restored));
        foreach (var changed in new[] { proof with { EvidenceId = Guid.NewGuid() }, proof with { CatalogVersion = 4 }, proof with { PolicyVersion = 3 }, proof with { BusinessDate = new(2026, 10, 21) }, proof with { ExpiryDate = null }, proof with { DecisionCode = "Allowed" } })
            Assert.NotEqual(hash, OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", frozen with { Items = [frozen.Items[0] with { ExpiryEvidence = changed }] }));
    }

    [Fact]
    public void Legacy_payload_hash_is_unchanged()
    {
        var hash = OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", Sale(calculationVersion: null));

        Assert.Equal(LegacyHash, hash);
    }

    [Fact]
    public void Calculation_version_changes_the_hash()
    {
        var legacy = OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", Sale(calculationVersion: null));
        var version1 = OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", Sale(calculationVersion: 1));

        Assert.NotEqual(legacy, version1);
        Assert.Equal(version1, OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", Sale(calculationVersion: 1)));
    }

    [Fact]
    public void LegacyPayload_HashUnchanged()
    {
        Assert.Equal(LegacyHash, OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", Sale(calculationVersion: null, issuedByUserId: null)));
        Assert.Equal(Version1WithoutIssuerHash, OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", Sale(calculationVersion: 1, issuedByUserId: null)));
    }

    [Fact]
    public void IssuedByUserId_ChangesHash()
    {
        var withoutIssuer = OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", Sale(calculationVersion: 1, issuedByUserId: null));
        var withIssuer = OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", Sale(calculationVersion: 1, issuedByUserId: IssuerId));
        var otherIssuer = OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", Sale(calculationVersion: 1, issuedByUserId: Guid.NewGuid()));

        Assert.NotEqual(withoutIssuer, withIssuer);
        Assert.NotEqual(withIssuer, otherIssuer);
        Assert.Equal(withIssuer, OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", Sale(calculationVersion: 1, issuedByUserId: IssuerId)));
    }

    [Fact]
    public void EnvelopePayload_RoundTripsToSameHash()
    {
        var sale = Sale(calculationVersion: 1, issuedByUserId: IssuerId) with
        {
            CustomerId = Guid.NewGuid(),
            PaymentMethod = PaymentMethod.Credit,
            PaidAmount = 18.50m,
            DueAmount = 100.00m,
            TotalDiscountAmount = 0.10m,
            SaleDiscountOverrideType = InstantDiscountType.Percentage,
            SaleDiscountOverrideValue = 2.5m,
            ConfiguredSaleRuleId = Guid.NewGuid(),
            ConfiguredSaleRuleType = DiscountRuleType.SaleThresholdPercentage,
            ConfiguredSaleRulePercentage = 7.25m,
            ConfiguredSaleRuleThresholdAmount = 1000.000m,
            Items =
            [
                .. Sale(calculationVersion: 1, issuedByUserId: null).Items,
                new OfflineSaleSyncLineCommand(
                    "SVC-001", "", "Repair", 1m, 0m, 500m, 0m, 18m, true, Guid.Empty,
                    500.00m, 0m, 0m, 423.73m, 76.27m, 500.00m, Guid.NewGuid(), 12.5m, InstantDiscountType.Flat, 3.40m, null,
                    LineType: "Service",
                    ServiceId: Guid.NewGuid()),
            ],
        };

        var json = OfflineSubmissionPayload.Serialize(sale);
        var replayed = OfflineSubmissionPayload.Deserialize(json);

        Assert.NotNull(replayed);
        Assert.Equal(
            OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", sale),
            OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", replayed));
        Assert.Equal(sale.SoldAt, replayed.SoldAt);
        Assert.Equal(sale.SoldAt.Offset, replayed.SoldAt.Offset);
        Assert.Equal(sale.IssuedByUserId, replayed.IssuedByUserId);
        Assert.Equal(sale.PaymentMethod, replayed.PaymentMethod);
        Assert.Equal(sale.Items[1].LineType, replayed.Items[1].LineType);
        Assert.Equal(sale.Items[1].ServiceId, replayed.Items[1].ServiceId);
        Assert.Equal(json, OfflineSubmissionPayload.Serialize(replayed));
    }

    [Fact]
    public void FrozenTillEvidence_ChangesHash()
    {
        var sale = Sale(1, IssuerId);
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(sale))!;
        node["Till"] = System.Text.Json.Nodes.JsonNode.Parse($"{{\"SessionId\":\"{Guid.NewGuid()}\"}}");
        node["TenderLines"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"Method\":2,\"AppliedAmount\":118,\"CashTendered\":200,\"Change\":82}]");
        var frozen = node.Deserialize<OfflineSaleSyncCommand>()!;
        Assert.NotEqual(OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", sale), OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", frozen));
    }

    [Fact]
    public void FrozenTillEnvelope_RoundTripsAndHashesEveryEvidenceField()
    {
        var sale = Sale(1, IssuerId) with
        {
            Till = new TillPostingContext(Guid.NewGuid(), " assistance "),
            TillBusinessDate = new DateOnly(2026, 10, 21),
            TillCashierId = IssuerId,
            TenderLines = [new(ReceiptTenderMethod.Cash, 118m, 200m, 82m, " ref ", null)]
        };
        var json = OfflineSubmissionPayload.Serialize(sale);
        var restored = OfflineSubmissionPayload.Deserialize(json)!;
        Assert.Equal(sale.Till, restored.Till);
        Assert.Equal(sale.TillBusinessDate, restored.TillBusinessDate);
        Assert.Equal(sale.TillCashierId, restored.TillCashierId);
        Assert.Equal(sale.TenderLines, restored.TenderLines);
        var hash = OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", sale);
        Assert.Equal(hash, OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", restored));
        var changes = new[]
        {
            sale with { Till = new(Guid.NewGuid(), sale.Till.OverrideReason) },
            sale with { Till = sale.Till with { OverrideReason = "different" } },
            sale with { TillBusinessDate = sale.TillBusinessDate.Value.AddDays(1) },
            sale with { TillCashierId = Guid.NewGuid() },
            sale with { TenderLines = [sale.TenderLines[0] with { CashTendered = 300m, Change = 182m }] },
            sale with { TenderLines = [sale.TenderLines[0] with { ExternalReference = "different" }] },
            sale with { TenderLines = [sale.TenderLines[0] with { BankAccountId = Guid.NewGuid() }] },
            sale with { TenderLines = [] },
            sale with { TenderLines = null },
        };
        Assert.All(changes, changed => Assert.NotEqual(hash, OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", changed)));
        var trimmed = sale with { Till = sale.Till with { OverrideReason = "assistance" }, TenderLines = [sale.TenderLines[0] with { ExternalReference = "ref" }] };
        Assert.Equal(hash, OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", trimmed));
        Assert.DoesNotContain("till", OfflineSubmissionPayload.Serialize(Sale(1)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryReadValidated_VerifiesHashShopAndJsonWithoutRewritingEnvelope()
    {
        var sale = Sale(calculationVersion: null, issuedByUserId: null);
        var json = OfflineSubmissionPayload.Serialize(sale);
        var hash = OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", sale);
        var now = new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);
        var submission = OfflineSubmission.Record(
            ShopId, "device-1", sale.ClientSaleId, sale.InvoiceNumber, hash, json,
            sale.SoldAt, now, OfflineSubmissionSource.Recovery, IssuerId, null, null, null, "op-1");

        var read = OfflineSubmissionPayload.TryReadValidated(ShopId, submission);
        Assert.NotNull(read);
        Assert.Equal(LegacyHash, OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", read!));
        Assert.Equal(json, submission.PayloadJson);

        Assert.Null(OfflineSubmissionPayload.TryReadValidated(Guid.NewGuid(), submission));
        var badHash = OfflineSubmission.Record(
            ShopId, "device-1", sale.ClientSaleId, sale.InvoiceNumber, new string('0', 64), json,
            sale.SoldAt, now, OfflineSubmissionSource.Recovery, IssuerId, null, null, null, "op-2");
        Assert.Null(OfflineSubmissionPayload.TryReadValidated(ShopId, badHash));
        var badJson = OfflineSubmission.Record(
            ShopId, "device-1", sale.ClientSaleId, sale.InvoiceNumber, hash, "{not-json",
            sale.SoldAt, now, OfflineSubmissionSource.Recovery, IssuerId, null, null, null, "op-3");
        Assert.Null(OfflineSubmissionPayload.TryReadValidated(ShopId, badJson));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[null]}")]
    public void TryReadValidated_ReturnsNullForMalformedRetainedItemCollections(string payloadJson)
    {
        var now = new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);
        var submission = OfflineSubmission.Record(
            ShopId, "device-1", "client-sale-1", "INV-2025-26-000001", new string('A', 64), payloadJson,
            now, now, OfflineSubmissionSource.Recovery, IssuerId, null, null, null, "op-malformed");

        Assert.Null(OfflineSubmissionPayload.TryReadValidated(ShopId, submission));
    }

    [Theory]
    [InlineData(null, LegacyHash)]
    [InlineData(1, Version1WithoutIssuerHash)]
    public void TryReadValidated_AcceptsOmittedHistoricalLineServiceFieldsWithGoodsDefaults(int? calculationVersion, string expectedHash)
    {
        var sale = Sale(calculationVersion, issuedByUserId: null);
        var root = System.Text.Json.Nodes.JsonNode.Parse(OfflineSubmissionPayload.Serialize(sale))!.AsObject();
        var line = root["items"]![0]!.AsObject();
        line.Remove("lineType");
        line.Remove("serviceId");
        var strippedJson = root.ToJsonString();

        var now = new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);
        var submission = OfflineSubmission.Record(
            ShopId, "device-1", sale.ClientSaleId, sale.InvoiceNumber, expectedHash, strippedJson,
            sale.SoldAt, now, OfflineSubmissionSource.Recovery, IssuerId, null, null, null, "op-omitted");

        var read = OfflineSubmissionPayload.TryReadValidated(ShopId, submission);
        Assert.NotNull(read);
        Assert.Equal("Goods", read!.Items[0].LineType);
        Assert.Null(read.Items[0].ServiceId);
        Assert.False(read.Items[0].IsServiceLine);
        Assert.Equal(expectedHash, OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", read));
        Assert.Equal(strippedJson, submission.PayloadJson);
    }

    [Theory]
    [InlineData("Service", null)]
    [InlineData("service", null)]
    [InlineData("Other", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("Goods", "11111111-1111-4111-8111-111111111111")]
    [InlineData("Goods", "00000000-0000-0000-0000-000000000000")]
    [InlineData("Service", "11111111-1111-4111-8111-111111111111")]
    public void TryReadValidated_RejectsIncompatibleLineServiceSemanticsEvenWithMatchingHash(string? lineType, string? serviceId)
    {
        var sale = Sale(calculationVersion: null, issuedByUserId: null);
        var root = System.Text.Json.Nodes.JsonNode.Parse(OfflineSubmissionPayload.Serialize(sale))!.AsObject();
        var line = root["items"]![0]!.AsObject();
        line["lineType"] = lineType is null ? null : System.Text.Json.Nodes.JsonValue.Create(lineType);
        line["serviceId"] = serviceId is null ? null : System.Text.Json.Nodes.JsonValue.Create(serviceId);
        var mutatedJson = root.ToJsonString();

        // Confirm the canonical v1 hash is unchanged by the two omitted properties.
        var deserialized = OfflineSubmissionPayload.Deserialize(mutatedJson)!;
        Assert.Equal(LegacyHash, OfflineSaleSyncIdempotencyHasher.ComputeHash(ShopId, "device-1", deserialized));

        var now = new DateTimeOffset(2026, 10, 21, 5, 0, 0, TimeSpan.Zero);
        var submission = OfflineSubmission.Record(
            ShopId, "device-1", sale.ClientSaleId, sale.InvoiceNumber, LegacyHash, mutatedJson,
            sale.SoldAt, now, OfflineSubmissionSource.Recovery, IssuerId, null, null, null, "op-service-tamper");

        Assert.Null(OfflineSubmissionPayload.TryReadValidated(ShopId, submission));
        Assert.Equal(mutatedJson, submission.PayloadJson);
        Assert.Equal(LegacyHash, submission.PayloadHash);
    }

    private static OfflineSaleSyncCommand Sale(int? calculationVersion, Guid? issuedByUserId = null) =>
        new(
            "client-sale-1",
            "INV-2025-26-000001",
            new DateTimeOffset(2026, 10, 21, 2, 0, 0, TimeSpan.FromHours(5.5)),
            null,
            "Ravi",
            "+919876543210",
            PaymentMethod.Cash,
            118m,
            0m,
            100m,
            100m,
            0m,
            18m,
            118m,
            InstantDiscountType.None,
            0m,
            null,
            null,
            null,
            null,
            [
                new OfflineSaleSyncLineCommand(
                    "BC-001",
                    "B-01",
                    "Rice",
                    1m,
                    80m,
                    100m,
                    120m,
                    18m,
                    false,
                    BatchId,
                    100m,
                    0m,
                    0m,
                    100m,
                    18m,
                    118m,
                    null,
                    null,
                    InstantDiscountType.None,
                    0m,
                    "1006"),
            ],
            calculationVersion,
            issuedByUserId);
}
