using System.Globalization;

namespace Intelibill.Application.Features.Inventory.Services;

public sealed class InventoryAdjustmentNumberGenerator : IInventoryAdjustmentNumberGenerator
{
    public string Generate(DateOnly businessDate) =>
        string.Create(CultureInfo.InvariantCulture, $"ADJ-{businessDate:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}");
}
