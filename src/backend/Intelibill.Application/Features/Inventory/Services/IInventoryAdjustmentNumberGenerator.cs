namespace Intelibill.Application.Features.Inventory.Services;

public interface IInventoryAdjustmentNumberGenerator
{
    string Generate(DateTimeOffset? now = null);
}
