using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Domain.Entities;

public sealed class TillSession : BaseEntity
{
    public Guid ShopId { get; private set; }
    public Guid TillId { get; private set; }
    public Guid CashierId { get; private set; }
    public string TillName { get; private set; } = string.Empty;
    public string CashierName { get; private set; } = string.Empty;
    public Guid OpenedBy { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string BusinessTimeZoneId { get; private set; } = string.Empty;
    public decimal OpeningFloat { get; private set; }
    public TillSessionState State { get; private set; }
    public long LastMovementSequence { get; private set; }
    public long SourceCount { get; private set; }
    public uint Version { get; private set; }
    public string? OverrideReason { get; private set; }
    private TillSession() { }
    public static ErrorOr<TillSession> Open(Guid shopId, Guid tillId, Guid cashierId, string tillName, string cashierName,
        Guid actorId, DateTimeOffset now, DateOnly businessDate, string zone, decimal openingFloat, string? overrideReason)
    {
        if (shopId == Guid.Empty || tillId == Guid.Empty || cashierId == Guid.Empty || actorId == Guid.Empty ||
            string.IsNullOrWhiteSpace(tillName) || tillName.Length > 100 || string.IsNullOrWhiteSpace(cashierName) || cashierName.Length > 200 ||
            string.IsNullOrWhiteSpace(zone) || zone.Length > 64 || overrideReason?.Trim().Length > 500 || !TillMoney.IsValid(openingFloat) || openingFloat < 0)
            return Error.Validation("Till.OpenInvalid", "Opening requires valid references, labels and a nonnegative INR float.");
        if (actorId != cashierId && (string.IsNullOrWhiteSpace(overrideReason) || overrideReason.Trim().Length > 500))
            return Error.Validation("Till.OverrideReasonRequired", "Acting for another cashier requires a reason.");
        return new TillSession { ShopId = shopId, TillId = tillId, CashierId = cashierId, TillName = tillName.Trim(),
            CashierName = cashierName.Trim(), OpenedBy = actorId, OpenedAt = now, BusinessDate = businessDate,
            BusinessTimeZoneId = zone, OpeningFloat = openingFloat, State = TillSessionState.Open, OverrideReason = overrideReason?.Trim() };
    }
    public ErrorOr<long> NextSequence()
    {
        if (State != TillSessionState.Open) return Error.Conflict("Till.SessionClosed", "A closed session is immutable.");
        return ++LastMovementSequence;
    }
    internal ErrorOr<Success> RegisterSource()
    {
        if (State != TillSessionState.Open) return Error.Conflict("Till.SessionClosed", "A closed session is immutable.");
        SourceCount++;
        return Result.Success;
    }
    public ErrorOr<TillClose> Close(TillTotals totals, decimal countedCash, Guid actorId, DateTimeOffset now, DateOnly today,
        string? reason, bool awaitingReview, bool finalizeOverdue, string? overrideReason = null)
    {
        if (State != TillSessionState.Open) return Error.Conflict("Till.SessionClosed", "The session is already closed.");
        if (!TillMoney.IsValid(countedCash) || countedCash < 0 || actorId == Guid.Empty || now < OpenedAt || today < BusinessDate)
            return Error.Validation("Till.CloseInvalid", "The counted amount and close time must be valid.");
        if (overrideReason?.Trim().Length > 500 || (actorId != CashierId && string.IsNullOrWhiteSpace(overrideReason)))
            return Error.Validation("Till.OverrideReasonRequired", "Closing another cashier's session requires a persisted reason.");
        var overdue = today > BusinessDate;
        var expected = OpeningFloat + totals.RecordedTenderTotals.GetValueOrDefault(ReceiptTenderMethod.Cash);
        if (reason?.Trim().Length > 500 || ((expected != countedCash || overdue) && string.IsNullOrWhiteSpace(reason)))
            return Error.Validation("Till.ReasonRequired", "Variance and overdue finalization require a reason.");
        if (overdue && !finalizeOverdue) return Error.Conflict("Till.Overdue", "An overdue session requires explicit finalization.");
        if (totals.MovementCount != LastMovementSequence)
            return Error.Conflict("Till.TotalsStale", "Close totals must include every recorded movement.");
        State = TillSessionState.Closed;
        return TillClose.Record(this, totals, expected, countedCash, actorId, now, reason?.Trim(), overdue, awaitingReview && expected != countedCash, overrideReason?.Trim());
    }
}
