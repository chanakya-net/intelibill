using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.ValueObjects;

public sealed record TillTotals(IReadOnlyDictionary<ReceiptTenderMethod, decimal> RecordedTenderTotals,
    IReadOnlyDictionary<string, decimal> PurposeTotals, decimal StoreCreditIssued, decimal StoreCreditRedeemed,
    decimal ReceivableChange, long MovementCount)
{
    public static TillTotals From(IReadOnlyList<TillMovement> movements, IReadOnlyList<TillSourceRecord> sources) =>
        new(new System.Collections.ObjectModel.ReadOnlyDictionary<ReceiptTenderMethod, decimal>(movements.GroupBy(x => x.Method).ToDictionary(x => x.Key, x => x.Sum(m => m.SignedAmount))),
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, decimal>(movements.GroupBy(x => x.Purpose).ToDictionary(x => x.Key, x => x.Sum(m => m.SignedAmount))),
            sources.Sum(x => x.Settlement.StoreCreditIssued), sources.Sum(x => x.Settlement.StoreCreditRedeemed),
            sources.Sum(x => x.Settlement.ReceivableChange), movements.Count);
}
