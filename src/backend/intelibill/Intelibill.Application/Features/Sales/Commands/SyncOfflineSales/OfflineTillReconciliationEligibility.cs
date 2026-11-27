using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Tills;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

/// <summary>
/// Read-only projection of the existing reviewed-posting gates. It reserves no drawer and authorizes
/// no settlement: the poster repeats the same predicates after taking the original session lock.
/// </summary>
public sealed class OfflineTillReconciliationEligibility(
    ITillSessionPolicy policy, ITillRepository tills, IShopRepository shops, IBusinessClock clock)
{
    public async Task<bool> CanReconcileAsync(Guid shopId, OfflineSaleSyncCommand sale, Guid issuer,
        DateTimeOffset now, CancellationToken ct)
    {
        if (!await policy.IsEnabledAsync(shopId, ct) || issuer == Guid.Empty || sale.SoldAt > now) return false;
        var shop = await shops.GetByIdAsync(shopId, ct);
        if (shop is null) return false;
        if (sale.Till is not { } claimed) return true; // Reasoned legacy settlement can be recorded separately.
        var date = clock.GetBusinessDate(sale.SoldAt, shop.BusinessTimeZoneId);
        var candidate = await tills.GetSessionAsync(shopId, claimed.SessionId, ct);
        if (candidate is null || !MatchesHistorical(shopId, sale, candidate, issuer, date, now)) return true;
        var close = await tills.GetCloseAsync(shopId, candidate.Id, ct);
        if (close is not null && sale.SoldAt > close.ClosedAt) return true;
        return !IsCurrentOpen(candidate, clock, now);
    }

    internal static bool MatchesHistorical(Guid shopId, OfflineSaleSyncCommand sale, TillSession session,
        Guid issuer, DateOnly businessDate, DateTimeOffset now) =>
        session.ShopId == shopId && sale.IssuedByUserId == issuer && session.CashierId == issuer &&
        sale.TillCashierId == session.CashierId && sale.TillBusinessDate == session.BusinessDate &&
        session.BusinessDate == businessDate && sale.SoldAt >= session.OpenedAt && sale.SoldAt <= now;

    internal static bool IsCurrentOpen(TillSession? session, IBusinessClock clock, DateTimeOffset now) =>
        session is { State: TillSessionState.Open } && session.BusinessDate == clock.GetBusinessDate(now, session.BusinessTimeZoneId);
}
