using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Intelibill.Domain.Entities;

/// <summary>
/// One append-only conflict or verification observation recorded for a reconciliation case's
/// historical licence review. Identical material facts on the same case deduplicate by
/// <see cref="Fingerprint"/> (excluding observation time) while preserving distinct findings.
/// Never stores raw signed tokens or unbounded client text.
/// </summary>
public sealed class LicenseHistoryReviewObservation
{
    public Guid Id { get; private set; }
    public Guid ShopId { get; private set; }
    public Guid CaseId { get; private set; }
    public Guid? OfflineSubmissionId { get; private set; }
    public DateTimeOffset ObservedAt { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string Subreason { get; private set; } = string.Empty;
    public string? ComparedField { get; private set; }
    public DateTimeOffset? WindowStart { get; private set; }
    public DateTimeOffset? WindowEnd { get; private set; }
    public long? ObservedAccountRevision { get; private set; }
    public long? ObservedShopRevision { get; private set; }
    public DateTimeOffset? SourceCompletionCutoff { get; private set; }
    public Guid? CompetingSubmissionId { get; private set; }
    public string Fingerprint { get; private set; } = string.Empty;

    private LicenseHistoryReviewObservation() { }

    public static LicenseHistoryReviewObservation Create(
        Guid shopId,
        Guid caseId,
        Guid? offlineSubmissionId,
        DateTimeOffset observedAt,
        string reasonCode,
        string subreason,
        string? comparedField = null,
        DateTimeOffset? windowStart = null,
        DateTimeOffset? windowEnd = null,
        long? observedAccountRevision = null,
        long? observedShopRevision = null,
        DateTimeOffset? sourceCompletionCutoff = null,
        Guid? competingSubmissionId = null)
    {
        var normalizedReason = reasonCode.Trim();
        var normalizedSubreason = subreason.Trim();
        var normalizedField = string.IsNullOrWhiteSpace(comparedField) ? null : comparedField.Trim();
        var normalizedObservedAt = Instant(observedAt);
        var normalizedStart = windowStart is { } s ? Instant(s) : (DateTimeOffset?)null;
        var normalizedEnd = windowEnd is { } e ? Instant(e) : (DateTimeOffset?)null;
        var normalizedCutoff = sourceCompletionCutoff is { } c ? Instant(c) : (DateTimeOffset?)null;
        var normalizedCompeting = competingSubmissionId is { } comp && comp != offlineSubmissionId ? comp : (Guid?)null;

        var fingerprint = ComputeFingerprint(
            offlineSubmissionId,
            normalizedReason,
            normalizedSubreason,
            normalizedField,
            normalizedStart,
            normalizedEnd,
            observedAccountRevision,
            observedShopRevision,
            normalizedCutoff,
            normalizedCompeting);

        return new LicenseHistoryReviewObservation
        {
            Id = Guid.NewGuid(),
            ShopId = shopId,
            CaseId = caseId,
            OfflineSubmissionId = offlineSubmissionId,
            ObservedAt = normalizedObservedAt,
            ReasonCode = normalizedReason,
            Subreason = normalizedSubreason,
            ComparedField = normalizedField,
            WindowStart = normalizedStart,
            WindowEnd = normalizedEnd,
            ObservedAccountRevision = observedAccountRevision,
            ObservedShopRevision = observedShopRevision,
            SourceCompletionCutoff = normalizedCutoff,
            CompetingSubmissionId = normalizedCompeting,
            Fingerprint = fingerprint,
        };
    }

    public static string ComputeFingerprint(
        Guid? offlineSubmissionId,
        string reasonCode,
        string subreason,
        string? comparedField,
        DateTimeOffset? windowStart,
        DateTimeOffset? windowEnd,
        long? observedAccountRevision,
        long? observedShopRevision,
        DateTimeOffset? sourceCompletionCutoff,
        Guid? competingSubmissionId)
    {
        var canonical = string.Join('|',
            offlineSubmissionId?.ToString("D") ?? string.Empty,
            reasonCode,
            subreason,
            comparedField ?? string.Empty,
            FormatInstant(windowStart),
            FormatInstant(windowEnd),
            observedAccountRevision?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            observedShopRevision?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            FormatInstant(sourceCompletionCutoff),
            competingSubmissionId?.ToString("D") ?? string.Empty);

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static DateTimeOffset Instant(DateTimeOffset value)
    {
        var ticks = value.UtcTicks;
        return new DateTimeOffset(ticks - ticks % 10, TimeSpan.Zero);
    }

    private static string FormatInstant(DateTimeOffset? value) =>
        value is { } instant
            ? Instant(instant).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture)
            : string.Empty;
}
