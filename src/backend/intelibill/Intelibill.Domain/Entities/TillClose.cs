using Intelibill.Domain.Common;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

public sealed class TillClose : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid SessionId { get; private set; }
    public decimal OpeningFloat { get; private set; }
    public long LastMovementSequence { get; private set; }
    public TillTotals Totals { get; private set; } = new(new Dictionary<Enums.ReceiptTenderMethod, decimal>(), new Dictionary<string, decimal>(), 0, 0, 0, 0);
    public decimal ExpectedCash { get; private set; }
    public decimal CountedCash { get; private set; }
    public decimal Variance { get; private set; }
    public Guid ClosedBy { get; private set; }
    public DateTimeOffset ClosedAt { get; private set; }
    public string? Reason { get; private set; }
    public string? OverrideReason { get; private set; }
    public bool IsOverdue { get; private set; }
    public bool AwaitingReview { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string TillName { get; private set; } = string.Empty;
    public string CashierName { get; private set; } = string.Empty;
    public Guid CashierId { get; private set; }
    private TillClose() { }
    internal static TillClose Record(TillSession session, TillTotals totals, decimal expectedCash, decimal countedCash,
        Guid actorId, DateTimeOffset now, string? reason, bool overdue, bool awaitingReview, string? overrideReason) =>
        new() { ShopId = session.ShopId, SessionId = session.Id, OpeningFloat = session.OpeningFloat,
            LastMovementSequence = session.LastMovementSequence, Totals = totals with {
                RecordedTenderTotals = new System.Collections.ObjectModel.ReadOnlyDictionary<Enums.ReceiptTenderMethod, decimal>(totals.RecordedTenderTotals.ToDictionary()),
                PurposeTotals = new System.Collections.ObjectModel.ReadOnlyDictionary<string, decimal>(totals.PurposeTotals.ToDictionary()) }, ExpectedCash = expectedCash,
            CountedCash = countedCash, Variance = countedCash - expectedCash, ClosedBy = actorId, ClosedAt = now,
            Reason = reason, OverrideReason = overrideReason, IsOverdue = overdue, AwaitingReview = awaitingReview, BusinessDate = session.BusinessDate,
            TillName = session.TillName, CashierName = session.CashierName, CashierId = session.CashierId };
}
