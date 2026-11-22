using ErrorOr;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Expiry;
using Intelibill.Application.Features.Tills;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using PermitErrors = Intelibill.Application.Common.Errors.Errors.OfflinePermit;

namespace Intelibill.Application.Common.Licensing.Offline;

/// <summary>
/// The business gates a sale takes after the licence fence, in the same order: shared expiry
/// fence, then the shop's till gate (held even when tills are off, so enabling them serializes
/// with this permit), then the caller's own open till session.
/// </summary>
public sealed class OfflinePermitGates(
    IExpiryPolicyFence expiryFence, ITillRepository tills, ITillSessionPolicy tillPolicy, IBusinessClock businessClock)
{
    /// <summary>The end of the till session's business day, or null when the shop runs no till sessions.</summary>
    public async Task<ErrorOr<DateTimeOffset?>> EnterAsync(Guid shopId, Guid userId, Guid? tillSessionId, DateTimeOffset now,
        CancellationToken ct)
    {
        var fenced = await expiryFence.AcquireAsync(shopId, false, ct);
        if (fenced.IsError)
            return fenced.Errors;
        if (await tills.LockSettingsReadAsync(shopId, ct) != RowLockOutcome.Acquired)
            return PermitErrors.Busy;
        if (!await tillPolicy.IsEnabledAsync(shopId, ct))
            return (DateTimeOffset?)null;

        if (tillSessionId is not { } id)
            return PermitErrors.TillRequired;
        var locked = await tills.LockSessionAsync(shopId, id, ct);
        if (locked != RowLockOutcome.Acquired)
            return locked == RowLockOutcome.TimedOut ? PermitErrors.Busy : PermitErrors.TillRequired;

        // Offline sales land in this cashier's drawer for this business date and no other.
        var session = await tills.ReloadSessionAsync(shopId, id, ct);
        if (session is null || session.CashierId != userId || session.State != TillSessionState.Open ||
            session.BusinessDate != businessClock.GetBusinessDate(now, session.BusinessTimeZoneId))
            return PermitErrors.TillRequired;

        return businessClock.GetUtcWindow(session.BusinessDate, session.BusinessDate, session.BusinessTimeZoneId).EndExclusive;
    }
}
