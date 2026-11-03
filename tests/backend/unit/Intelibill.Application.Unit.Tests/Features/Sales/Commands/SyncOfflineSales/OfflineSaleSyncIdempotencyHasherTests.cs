using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
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
