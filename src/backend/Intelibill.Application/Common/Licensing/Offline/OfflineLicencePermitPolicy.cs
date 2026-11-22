using ErrorOr;
using Intelibill.Shared.Contracts.V1;
using PermitErrors = Intelibill.Application.Common.Errors.Errors.OfflinePermit;

namespace Intelibill.Application.Common.Licensing.Offline;

/// <summary>
/// Every server fact that can end an offline window. Each is required: an unknown cap is a refusal,
/// never "no limit". The two optional members apply only where the note says so.
/// </summary>
/// <param name="AuthorityVerifiedAt">When licensing verified the original grant's older head.</param>
/// <param name="LicenceValidUntil">The original grant's deadline: licence end, next boundary or scheduled suspension.</param>
/// <param name="CurrentLicenceValidUntil">The same deadline from the head fenced now; only ever shortens.</param>
/// <param name="DeviceVerifiedAt">The server clock when this device reserved the lease online.</param>
/// <param name="AuthDeadline">Earliest of the frozen and current session and refresh-credential ends.</param>
/// <param name="LeaseExpiresAt">The reserved invoice lease's own expiry.</param>
/// <param name="FiscalYearEndsAt">Start of the fiscal year after the leased series.</param>
/// <param name="SnapshotCompletedAt">When the server finished the stock snapshot the device holds.</param>
/// <param name="EarliestStockEvidenceExpiry">First expiry among the snapshot's retained lot evidence; null only for a complete empty snapshot.</param>
/// <param name="TillDayEndsAt">End of the open till session's business day; null only when the shop runs no till sessions.</param>
public sealed record OfflinePermitCaps(
    DateTimeOffset AuthorityVerifiedAt, DateTimeOffset LicenceValidUntil, DateTimeOffset CurrentLicenceValidUntil,
    DateTimeOffset DeviceVerifiedAt, DateTimeOffset AuthDeadline, DateTimeOffset LeaseExpiresAt,
    DateTimeOffset FiscalYearEndsAt, DateTimeOffset SnapshotCompletedAt, DateTimeOffset? EarliestStockEvidenceExpiry,
    DateTimeOffset? TillDayEndsAt);

public readonly record struct OfflinePermitWindow(DateTimeOffset NotBefore, DateTimeOffset ValidUntil, string LimitingReason);

/// <summary>The earliest of every cap, as a half-open window starting at issue.</summary>
public static class OfflineLicencePermitPolicy
{
    public static readonly TimeSpan Freshness = EntitlementSnapshotsV1.Freshness;
    public static readonly TimeSpan SnapshotLifetime = TimeSpan.FromHours(48);

    public const string AuthorityFreshness = "AuthorityFreshness";
    public const string LicenceEnd = "LicenceEnd";
    public const string CurrentLicenceEnd = "CurrentLicenceEnd";
    public const string DeviceFreshness = "DeviceFreshness";
    public const string AuthDeadline = "AuthDeadline";
    public const string LeaseExpiry = "LeaseExpiry";
    public const string FiscalYearEnd = "FiscalYearEnd";
    public const string SnapshotLifetimeEnd = "SnapshotLifetime";
    public const string StockEvidenceExpiry = "StockEvidenceExpiry";
    public const string TillDayEnd = "TillDayEnd";

    /// <summary>Caps in tie order: when two end at the same instant the earlier entry names the reason.</summary>
    public static IReadOnlyList<(string Reason, DateTimeOffset Deadline)> Deadlines(OfflinePermitCaps caps)
    {
        var deadlines = new List<(string, DateTimeOffset)>(10)
        {
            (AuthorityFreshness, caps.AuthorityVerifiedAt + Freshness),
            (LicenceEnd, caps.LicenceValidUntil),
            (CurrentLicenceEnd, caps.CurrentLicenceValidUntil),
            (DeviceFreshness, caps.DeviceVerifiedAt + Freshness),
            (AuthDeadline, caps.AuthDeadline),
            (LeaseExpiry, caps.LeaseExpiresAt),
            (FiscalYearEnd, caps.FiscalYearEndsAt),
            (SnapshotLifetimeEnd, caps.SnapshotCompletedAt + SnapshotLifetime),
        };
        if (caps.EarliestStockEvidenceExpiry is { } stock) deadlines.Add((StockEvidenceExpiry, stock));
        if (caps.TillDayEndsAt is { } till) deadlines.Add((TillDayEnd, till));
        return deadlines;
    }

    public static ErrorOr<OfflinePermitWindow> Evaluate(OfflinePermitCaps caps, DateTimeOffset issuedAt)
    {
        // Verification, reservation and capture all precede issue; anything else is not this server's evidence.
        if (caps.AuthorityVerifiedAt == default || caps.AuthorityVerifiedAt > caps.DeviceVerifiedAt ||
            caps.DeviceVerifiedAt > caps.SnapshotCompletedAt || caps.SnapshotCompletedAt > issuedAt)
            return PermitErrors.AuthorityUnavailable;

        var (reason, validUntil) = Deadlines(caps).MinBy(cap => cap.Deadline);
        validUntil = OfflinePermitContractV1.Instant(validUntil);
        return issuedAt < validUntil
            ? new OfflinePermitWindow(issuedAt, validUntil, reason)
            : PermitErrors.DeadlineReached(reason);
    }

    /// <summary>Half-open: valid at <c>NotBefore</c>, not at <c>ValidUntil</c>.</summary>
    public static bool Covers(DateTimeOffset notBefore, DateTimeOffset validUntil, DateTimeOffset at) =>
        notBefore <= at && at < validUntil;
}
