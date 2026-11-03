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

    private static OfflineSaleSyncCommand Sale(int? calculationVersion) =>
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
            calculationVersion);
}
