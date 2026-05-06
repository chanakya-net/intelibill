namespace Intelibill.Application.Features.Inventory.Services;

public sealed class InventoryAdjustmentNumberGenerator : IInventoryAdjustmentNumberGenerator
{
    public string Generate(DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        return $"ADJ-{timestamp.UtcDateTime:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
    }
}
