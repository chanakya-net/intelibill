using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;
using Intelibill.Domain.ValueObjects;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

/// <summary>
/// Decides whether an offline invoice's claimed issue time can be trusted against the lease that
/// reserved its number. A plausible clock value never passes on its own: an invoice first received
/// after its lease expired is reviewed even when it claims an issue time inside the lease.
/// </summary>
internal static class OfflineIssueTimingPolicy
{
    /// <summary>How far a device clock may run ahead of the server's first receipt.</summary>
    public static readonly TimeSpan ClockSkewAllowance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Null to accept; otherwise the reason to hold the invoice. Rules apply in order.
    /// </summary>
    /// <param name="lease">The lease whose range holds the invoice number, expired or not.</param>
    /// <param name="claimedSoldAt">The issue time the device recorded.</param>
    /// <param name="firstReceivedAt">When the server first stored this exact payload.</param>
    public static Error? Evaluate(InvoiceLease lease, DateTimeOffset claimedSoldAt, DateTimeOffset firstReceivedAt)
    {
        if (claimedSoldAt < lease.ReservedAt)
            return Errors.OfflineReconciliation.IssueTimeBeforeLease;

        if (claimedSoldAt > firstReceivedAt + ClockSkewAllowance)
            return Errors.OfflineReconciliation.IssueTimeInFuture;

        if (FiscalYear.ForDate(claimedSoldAt).StartYear != lease.FiscalYearStart)
            return Errors.OfflineReconciliation.IssueFiscalYearMismatch;

        if (claimedSoldAt >= lease.ExpiresAt)
            return Errors.OfflineReconciliation.IssuedAfterLeaseExpiry;

        if (firstReceivedAt >= lease.ExpiresAt)
            return Errors.OfflineReconciliation.HistoricalLeaseReviewRequired;

        return null;
    }
}
