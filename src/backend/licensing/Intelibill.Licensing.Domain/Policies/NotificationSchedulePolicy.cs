using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Intelibill.Licensing.Domain.ValueObjects;

namespace Intelibill.Licensing.Domain.Policies;

public enum NoticeRestrictionScope
{
    None = 0,
    Partial = 1,
    Full = 2,
}

public sealed record PlannedThreshold(
    string Purpose,
    string SourceKind,
    string Threshold,
    DateTimeOffset DueAt,
    DateTimeOffset EffectiveDeadlineAt,
    string AccessStateAtDue,
    string GuidanceCode,
    string TemplateKey,
    bool PaymentDoesNotLiftSuspension);

/// <summary>
/// Pure, deterministic threshold planner over <see cref="SubscriptionAccessPolicy"/> and <see cref="GracePolicy"/>.
/// Preplans future useful thresholds, deduplicates coincident lifecycle instants, never assigns paid grace to
/// trial or complimentary access, and never suggests payment lifts an active suspension.
/// </summary>
public static class NotificationSchedulePolicy
{
    public const string RevisionVersion = "v1";

    public static IReadOnlyList<PlannedThreshold> PlanThresholds(
        SubscriptionAccessFacts facts,
        DateTimeOffset now,
        bool isSuspended,
        bool isClosed) =>
        PlanThresholds(
            facts,
            now,
            isSuspended ? NoticeRestrictionScope.Full : NoticeRestrictionScope.None,
            isClosed);

    public static IReadOnlyList<PlannedThreshold> PlanThresholds(
        SubscriptionAccessFacts facts,
        DateTimeOffset now,
        NoticeRestrictionScope restriction,
        bool isClosed)
    {
        if (facts.Evidence != TermEvidenceState.Complete || isClosed || facts.Basis is null)
            return [];

        var evaluated = SubscriptionAccessPolicy.Evaluate(facts, now);
        var candidates = facts.Basis.Value switch
        {
            SubscriptionBasis.Trial => TrialCandidates(evaluated, restriction),
            SubscriptionBasis.Paid => PaidCandidates(facts, evaluated, restriction),
            _ => [],
        };

        return candidates.Count == 0 ? [] : DeduplicateAndFilter(candidates, now);
    }

    public static string BuildCommercialSourceRevision(
        string sourceKind,
        Guid sourceId,
        SubscriptionAccessFacts facts,
        int trialExtensionRevision,
        long cancellationVersion,
        long? pricePaise,
        string? currency)
    {
        var evaluated = SubscriptionAccessPolicy.Evaluate(facts, facts.StartsAt);
        var payload = string.Join(
            '|',
            "commercial",
            sourceKind,
            sourceId.ToString("D"),
            trialExtensionRevision.ToString(CultureInfo.InvariantCulture),
            cancellationVersion.ToString(CultureInfo.InvariantCulture),
            evaluated.StartsAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            evaluated.PaidThrough.UtcTicks.ToString(CultureInfo.InvariantCulture),
            (evaluated.GraceEndsAt?.UtcTicks ?? -1L).ToString(CultureInfo.InvariantCulture),
            (pricePaise ?? -1L).ToString(CultureInfo.InvariantCulture),
            currency ?? string.Empty);
        return $"{RevisionVersion}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))}";
    }

    // A reminder belongs to this term, even when access was continuous across earlier paid terms.
    public static SubscriptionAccessFacts OperationalFacts(
        SubscriptionAccessFacts facts,
        DateTimeOffset termStart,
        DateTimeOffset? coverageFrom,
        DateTimeOffset? handoffEndsAt) =>
        facts.Basis == SubscriptionBasis.Paid
            ? facts with
            {
                StartsAt = coverageFrom is { } joined && joined > termStart && joined < facts.EndsAt ? joined : termStart,
                HandoffEndsAt = handoffEndsAt,
            }
            : facts;

    public static string BuildOperationalSourceRevision(
        string sourceKind,
        Guid sourceId,
        SubscriptionAccessFacts facts,
        int trialExtensionRevision,
        long cancellationVersion)
    {
        var evaluated = SubscriptionAccessPolicy.Evaluate(facts, facts.StartsAt);
        var payload = string.Join(
            '|',
            "operational",
            sourceKind,
            sourceId.ToString("D"),
            trialExtensionRevision.ToString(CultureInfo.InvariantCulture),
            cancellationVersion.ToString(CultureInfo.InvariantCulture),
            evaluated.StartsAt.UtcTicks.ToString(CultureInfo.InvariantCulture),
            evaluated.PaidThrough.UtcTicks.ToString(CultureInfo.InvariantCulture),
            (evaluated.GraceEndsAt?.UtcTicks ?? -1L).ToString(CultureInfo.InvariantCulture),
            (facts.HandoffEndsAt?.UtcTicks ?? -1L).ToString(CultureInfo.InvariantCulture));
        return $"{RevisionVersion}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))}";
    }

    /// <summary>
    /// Maps a base threshold guidance code to the live restriction-safe guidance and state without
    /// altering the immutable semantic job key.
    /// </summary>
    public static (string AccessState, string GuidanceCode, bool PaymentDoesNotLiftSuspension) ProjectLiveRestriction(
        string threshold,
        string baseAccessState,
        string baseGuidanceCode,
        NoticeRestrictionScope restriction)
    {
        if (restriction == NoticeRestrictionScope.Full)
            return (nameof(SubscriptionAccessState.Suspended), "SuspendedNoPaymentLift", true);

        if (baseGuidanceCode == "SuspendedNoPaymentLift")
        {
            (baseAccessState, baseGuidanceCode) = threshold switch
            {
                "TrialMinus3Days" or "TrialMinus1Day" => (nameof(SubscriptionAccessState.Active), "TrialEnding"),
                "GraceExpiry" => (nameof(SubscriptionAccessState.Expired), "AccessExpired"),
                _ => (baseAccessState, baseGuidanceCode),
            };
        }

        if (restriction == NoticeRestrictionScope.None)
            return (baseAccessState, StripPartialSuffix(baseGuidanceCode), false);

        var partialGuidance = baseGuidanceCode switch
        {
            "TrialEnding" or "TrialEndingPartialSuspension" => "TrialEndingPartialSuspension",
            "PaidRenewalDue" or "PaidRenewalPartialSuspension" => "PaidRenewalPartialSuspension",
            "GraceActive" or "GraceActivePartialSuspension" => "GraceActivePartialSuspension",
            "GraceEnding" or "GraceEndingPartialSuspension" => "GraceEndingPartialSuspension",
            _ => baseGuidanceCode,
        };
        return (baseAccessState, partialGuidance, true);
    }

    public static string CurrentTemplateKey(string storedKey, string guidance) =>
        guidance == "SuspendedNoPaymentLift"
            ? storedKey switch
            {
                "notice.trial.minus3" => "notice.suspended.trial.minus3",
                "notice.trial.minus1" => "notice.suspended.trial.minus1",
                "notice.grace.expiry" => "notice.suspended.grace.expiry",
                _ => storedKey,
            }
            : storedKey.Replace("notice.suspended.", "notice.", StringComparison.Ordinal);

    private static string StripPartialSuffix(string code) => code switch
    {
        "TrialEndingPartialSuspension" => "TrialEnding",
        "PaidRenewalPartialSuspension" => "PaidRenewalDue",
        "GraceActivePartialSuspension" => "GraceActive",
        "GraceEndingPartialSuspension" => "GraceEnding",
        _ => code,
    };

    private static List<CandidateThreshold> TrialCandidates(
        SubscriptionAccessEvaluation evaluated,
        NoticeRestrictionScope restriction)
    {
        var full = restriction == NoticeRestrictionScope.Full;
        var guidance = restriction switch
        {
            NoticeRestrictionScope.Full => "SuspendedNoPaymentLift",
            NoticeRestrictionScope.Partial => "TrialEndingPartialSuspension",
            _ => "TrialEnding",
        };
        var state = full ? nameof(SubscriptionAccessState.Suspended) : nameof(SubscriptionAccessState.Active);
        var noLift = restriction != NoticeRestrictionScope.None;
        var results = new List<CandidateThreshold>(2);
        AddCandidate(
            results,
            evaluated.StartsAt,
            priority: 1,
            new PlannedThreshold(
                "TrialReminder",
                "Trial",
                "TrialMinus3Days",
                evaluated.PaidThrough.AddDays(-3),
                evaluated.PaidThrough,
                state,
                guidance,
                full ? "notice.suspended.trial.minus3" : "notice.trial.minus3",
                noLift));
        AddCandidate(
            results,
            evaluated.StartsAt,
            priority: 2,
            new PlannedThreshold(
                "TrialReminder",
                "Trial",
                "TrialMinus1Day",
                evaluated.PaidThrough.AddDays(-1),
                evaluated.PaidThrough,
                state,
                guidance,
                full ? "notice.suspended.trial.minus1" : "notice.trial.minus1",
                noLift));
        return results;
    }

    private static List<CandidateThreshold> PaidCandidates(
        SubscriptionAccessFacts facts,
        SubscriptionAccessEvaluation evaluated,
        NoticeRestrictionScope restriction)
    {
        if (facts.RenewedAtEnd)
            return [];

        var results = new List<CandidateThreshold>(5);
        AddPaidRenewalCandidates(results, facts, evaluated, restriction);
        AddGraceCandidates(results, evaluated, restriction);
        return results;
    }

    private static void AddPaidRenewalCandidates(
        List<CandidateThreshold> results,
        SubscriptionAccessFacts facts,
        SubscriptionAccessEvaluation evaluated,
        NoticeRestrictionScope restriction)
    {
        // Full suspension suppresses payment-invitation reminders so no notice implies paying removes suspension.
        if (restriction == NoticeRestrictionScope.Full)
            return;

        var cancelled = facts.Cancellation is { IsEffective: true } cancellation
            && cancellation.EffectiveAt <= facts.EndsAt;
        var noLift = restriction == NoticeRestrictionScope.Partial;
        var guidance = cancelled
            ? "PaidTermCancelled"
            : noLift ? "PaidRenewalPartialSuspension" : "PaidRenewalDue";

        AddCandidate(
            results,
            evaluated.StartsAt,
            priority: 3,
            new PlannedThreshold(
                "PaidRenewalReminder",
                "Paid",
                "PaidMinus7Days",
                evaluated.PaidThrough.AddDays(-7),
                evaluated.PaidThrough,
                nameof(SubscriptionAccessState.Active),
                guidance,
                cancelled ? "notice.paid.cancelled.minus7" : "notice.paid.minus7",
                noLift));
        AddCandidate(
            results,
            evaluated.StartsAt,
            priority: 4,
            new PlannedThreshold(
                "PaidRenewalReminder",
                "Paid",
                "PaidMinus1Day",
                evaluated.PaidThrough.AddDays(-1),
                evaluated.PaidThrough,
                nameof(SubscriptionAccessState.Active),
                guidance,
                cancelled ? "notice.paid.cancelled.minus1" : "notice.paid.minus1",
                noLift));
    }

    private static void AddGraceCandidates(
        List<CandidateThreshold> results,
        SubscriptionAccessEvaluation evaluated,
        NoticeRestrictionScope restriction)
    {
        if (evaluated.GraceEndsAt is not { } graceEnds || graceEnds <= evaluated.PaidThrough)
            return;

        var full = restriction == NoticeRestrictionScope.Full;
        var noLift = restriction != NoticeRestrictionScope.None;
        if (!full)
        {
            AddCandidate(
                results,
                evaluated.StartsAt,
                priority: 6,
                new PlannedThreshold(
                    "GraceNotice",
                    "Paid",
                    "GraceStart",
                    evaluated.PaidThrough,
                    graceEnds,
                    nameof(SubscriptionAccessState.Grace),
                    noLift ? "GraceActivePartialSuspension" : "GraceActive",
                    "notice.grace.start",
                    noLift));
            AddCandidate(
                results,
                evaluated.PaidThrough,
                priority: 5,
                new PlannedThreshold(
                    "GraceNotice",
                    "Paid",
                    "GraceMinus1Day",
                    graceEnds.AddDays(-1),
                    graceEnds,
                    nameof(SubscriptionAccessState.Grace),
                    noLift ? "GraceEndingPartialSuspension" : "GraceEnding",
                    "notice.grace.minus1",
                    noLift));
        }

        AddCandidate(
            results,
            evaluated.PaidThrough,
            priority: 7,
            new PlannedThreshold(
                "GraceNotice",
                "Paid",
                "GraceExpiry",
                graceEnds,
                graceEnds,
                full ? nameof(SubscriptionAccessState.Suspended) : nameof(SubscriptionAccessState.Expired),
                full ? "SuspendedNoPaymentLift" : "AccessExpired",
                full ? "notice.suspended.grace.expiry" : "notice.grace.expiry",
                noLift));
    }

    private static void AddCandidate(
        List<CandidateThreshold> list,
        DateTimeOffset floor,
        int priority,
        PlannedThreshold plan)
    {
        if (plan.DueAt >= floor)
            list.Add(new CandidateThreshold(priority, plan));
    }

    private static IReadOnlyList<PlannedThreshold> DeduplicateAndFilter(
        List<CandidateThreshold> candidates,
        DateTimeOffset now)
    {
        var byInstant = new Dictionary<DateTimeOffset, CandidateThreshold>();
        foreach (var candidate in candidates)
        {
            if (candidate.Plan.DueAt < now)
                continue;
            if (!byInstant.TryGetValue(candidate.Plan.DueAt, out var existing)
                || candidate.Priority > existing.Priority)
            {
                byInstant[candidate.Plan.DueAt] = candidate;
            }
        }

        return
        [
            .. byInstant.Values
                .OrderBy(item => item.Plan.DueAt)
                .ThenBy(item => item.Priority)
                .Select(item => item.Plan),
        ];
    }

    private sealed record CandidateThreshold(int Priority, PlannedThreshold Plan);
}
