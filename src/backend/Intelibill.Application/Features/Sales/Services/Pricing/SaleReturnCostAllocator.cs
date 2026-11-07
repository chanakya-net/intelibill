using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Services.Pricing;

/// <summary>
/// Assigns the original rounded sale-line cost to dated return facts. Prior assignments never change;
/// voids reverse their exact assignment and release its quantity/cost for a later return.
/// </summary>
internal static class SaleReturnCostAllocator
{
    internal static IReadOnlyDictionary<(Guid ReturnId, Guid SaleItemId), decimal> Allocate(Sale sale, IReadOnlyList<SaleReturn> history)
    {
        var soldItems = sale.Items.Where(i => i.LineType != SaleLineType.Service).ToDictionary(i => i.Id);
        var states = new Dictionary<Guid, (decimal Quantity, decimal Cost)>();
        var allocated = new Dictionary<(Guid, Guid), decimal>();
        var events = new List<CostEvent>();
        foreach (var item in history)
        {
            events.Add(new(item.ProcessedAt, item, false));
            if (item.IsVoided && item.VoidedAt is { } voidedAt) events.Add(new(voidedAt, item, true));
        }
        foreach (var posting in events.OrderBy(e => e.At).ThenBy(e => e.Return.ReturnNumber, StringComparer.Ordinal).ThenBy(e => e.IsVoid).ThenBy(e => e.Return.Id))
        {
            foreach (var line in posting.Return.Items)
            {
                if (!soldItems.TryGetValue(line.SaleItemId, out var sold) || sold.Quantity <= 0m) continue;
                var state = states.GetValueOrDefault(sold.Id);
                var key = (posting.Return.Id, sold.Id);
                if (posting.IsVoid)
                {
                    states[sold.Id] = (state.Quantity - line.Quantity, state.Cost - allocated.GetValueOrDefault(key));
                    continue;
                }
                var quantity = state.Quantity + line.Quantity;
                var originalCost = SaleEconomicsCalculator.RecordedCost(sold.Quantity, sold.CostPrice);
                var cumulativeCost = decimal.Round(originalCost * quantity / sold.Quantity, 2, MidpointRounding.AwayFromZero);
                allocated[key] = cumulativeCost - state.Cost;
                states[sold.Id] = (quantity, cumulativeCost);
            }
        }
        return allocated;
    }

    private sealed record CostEvent(DateTimeOffset At, SaleReturn Return, bool IsVoid);
}
