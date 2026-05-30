using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Dashboard.Services;

internal static class StockRiskKpiCalculator
{
    internal sealed record StockRiskKpis(
        int RunningLowStockCount,
        int CriticalStockCount,
        IReadOnlyList<StockShortageItemDto> RankedShortageList);

    internal static StockRiskKpis CalculateStockRisk(IReadOnlyCollection<Domain.Entities.Inventory> inventories)
    {
        var runningLow = inventories.Where(i => i.Quantity > 0 && i.Quantity <= i.ReorderLevel).ToList();
        var critical = inventories.Where(i => i.Quantity == 0).ToList();
        var ranked = inventories
            .Where(i => i.Quantity <= i.ReorderLevel)
            .OrderByDescending(i => i.ReorderLevel - i.Quantity)
            .Select(i => new StockShortageItemDto(
                ItemName: i.Item.Name,
                Quantity: i.Quantity,
                ReorderLevel: i.ReorderLevel,
                Shortage: i.ReorderLevel - i.Quantity))
            .ToList();

        return new StockRiskKpis(runningLow.Count, critical.Count, ranked);
    }
}
