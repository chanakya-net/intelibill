using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Intelibill.Licensing.Domain.ValueObjects;

namespace Intelibill.Licensing.Domain.Policies;

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
        bool isClosed)
    {
        if (facts.Evidence != TermEvidenceState.Complete || isClosed || facts.Basis is null)
            return [];

        var evaluated = SubscriptionAccessPolicy.Evaluate(facts, now);
        var candidates = facts.Basis.Value switch
        {
            SubscriptionBasis.Trial => TrialCandidates(evaluated, isSuspended),
            SubscriptionBasis.Paid => PaidCandidates(facts, evaluated, isSuspended),
            _ => [],
        };

        if (candidates.Count == 0)
            return [];

        return DeduplicateAndFilter(candidates, now);
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

    private static List<CandidateThreshold> TrialCandidates(
        SubscriptionAccessEvaluation evaluated,
        bool isSuspended)
    {
        var guidance = isSuspended ? "SuspendedNoPaymentLift" : "TrialEnding";
        var state = isSuspended ? nameof(SubscriptionAccessState.Suspended) : nameof(SubscriptionAccessState.Active);
        var results = new List<CandidateThreshold>(2);
        AddIfAtOrAfter(
            results,
            evaluated.StartsAt,
            new CandidateThreshold(
                Priority: 1,
                new PlannedThreshold(
                    "TrialReminder",
                    "Trial",
                    "TrialMinus3Days",
                    evaluated.PaidThrough.AddDays(-3),
                    evaluated.PaidThrough,
                    state,
                    guidance,
                    isSuspended ? "notice.suspended.trial.minus3" : "notice.trial.minus3",
                    isSuspended)));
        AddIfAtOrAfter(
            results,
            evaluated.StartsAt,
            new CandidateThreshold(
                Priority: 2,
                new PlannedThreshold(
                    "TrialReminder",
                    "Trial",
                    "TrialMinus1Day",
                    evaluated.PaidThrough.AddDays(-1),
                    evaluated.PaidThrough,
                    state,
                    guidance,
                    isSuspended ? "notice.suspended.trial.minus1" : "notice.trial.minus1",
                    isSuspended)));
        return results;
    }

    private static List<CandidateThreshold> PaidCandidates(
        SubscriptionAccessFacts facts,
        SubscriptionAccessEvaluation evaluated,
        bool isSuspended)
    {
        if (facts.RenewedAtEnd)
            return [];

        var results = new List<CandidateThreshold>(5);
        var cancelled = facts.Cancellation is { IsEffective: true } cancellation
            && cancellation.EffectiveAt <= facts.EndsAt;

        // Active suspension suppresses payment-invitation reminders so no notice implies paying removes suspension.
        if (!isSuspended)
        {
            var paidGuidance = cancelled ? "PaidTermCancelled" : "PaidRenewalDue";
            AddIfAtOrAfter(
                results,
                evaluated.StartsAt,
                new CandidateThreshold(
                    Priority: 3,
                    new PlannedThreshold(
                        "PaidRenewalReminder",
                        "Paid",
                        "PaidMinus7Days",
                        evaluated.PaidThrough.AddDays(-7),
                        evaluated.PaidThrough,
                        nameof(SubscriptionAccessState.Active),
                        paidGuidance,
                        cancelled ? "notice.paid.cancelled.minus7" : "notice.paid.minus7",
                        false)));
            AddIfAtOrAfter(
                results,
                evaluated.StartsAt,
                new CandidateThreshold(
                    Priority: 4,
                    new PlannedThreshold(
                        "PaidRenewalReminder",
                        "Paid",
                        "PaidMinus1Day",
                        evaluated.PaidThrough.AddDays(-1),
                        evaluated.PaidThrough,
                        nameof(SubscriptionAccessState.Active),
                        paidGuidance,
                        cancelled ? "notice.paid.cancelled.minus1" : "notice.paid.minus1",
                        false)));
        }

        if (evaluated.GraceEndsAt is not { } graceEnds || graceEnds <= evaluated.PaidThrough)
            return results;

        if (!isSuspended)
        {
            AddIfAtOrAfter(
                results,
                evaluated.StartsAt,
                new CandidateThreshold(
                    Priority: 6,
                    new PlannedThreshold(
                        "GraceNotice",
                        "Paid",
                        "GraceStart",
                        evaluated.PaidThrough,
                        graceEnds,
                        nameof(SubscriptionAccessState.Grace),
                        "GraceActive",
                        "notice.grace.start",
                        false)));
            AddIfAtOrAfter(
                results,
                evaluated.PaidThrough,
                new CandidateThreshold(
                    Priority: 5,
                    new PlannedThreshold(
                        "GraceNotice",
                        "Paid",
                        "GraceMinus1Day",
                        graceEnds.AddDays(-1),
                        graceEnds,
                        nameof(SubscriptionAccessState.Grace),
                        "GraceEnding",
                        "notice.grace.minus1",
                        false)));
        }

        AddIfAtOrAfter(
            results,
            evaluated.PaidThrough,
            new CandidateThreshold(
                Priority: 7,
                new PlannedThreshold(
                    "GraceNotice",
                    "Paid",
                    "GraceExpiry",
                    graceEnds,
                    graceEnds,
                    isSuspended ? nameof(SubscriptionAccessState.Suspended) : nameof(SubscriptionAccessState.Expired),
                    isSuspended ? "SuspendedNoPaymentLift" : "AccessExpired",
                    isSuspended ? "notice.suspended.grace.expiry" : "notice.grace.expiry",
                    isSuspended)));

        return results;
    }

    private static void AddIfAtOrAfter(
        List<CandidateThreshold> list,
        DateTimeOffset floor,
        CandidateThreshold candidate)
    {
        if (candidate.Plan.DueAt >= floor)
            list.Add(candidate);
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
