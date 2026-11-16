using Intelibill.Licensing.Domain.ValueObjects;

namespace Intelibill.Licensing.Domain.Policies;

/// <summary>
/// Pure rules for dated complimentary (courtesy) access. A grant names its own start, end-exclusive end, shop
/// capacity and member allowance; it records no money and never receives grace. Source choice happens at one
/// evaluation instant and never displaces a baseline (paid or trial) term that already supplies access or a
/// restriction; suspension is applied to whichever source is chosen, so a grant can never conceal it.
/// </summary>
public static class ComplimentaryAccessPolicy
{
    public const string Version = "complimentary-access/1";
    public const int MaximumShops = 100;
    public const int MaximumMemberAllowance = 100_000;
    public const int MaximumReasonLength = 500;

    /// <summary>The grant interval and capacity are explicit, coherent and stored without precision loss.</summary>
    public static bool ValidInterval(DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        startsAt.Offset == TimeSpan.Zero && endsAt.Offset == TimeSpan.Zero
        && startsAt.UtcTicks % 10 == 0 && endsAt.UtcTicks % 10 == 0
        && startsAt > DateTimeOffset.UnixEpoch && endsAt < new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero)
        && startsAt < endsAt;

    public static bool ValidCapacity(int shops, int memberAllowance) =>
        shops is >= 1 and <= MaximumShops && memberAllowance is >= 1 and <= MaximumMemberAllowance;

    public static string CoverageMode(int shops) => shops == 1 ? "Independent" : "Grouped";

    /// <summary>Two windows for the same shop conflict unless they are disjoint; adjacent windows are legal.</summary>
    public static bool Overlaps(DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset otherStarts, DateTimeOffset otherEnds) =>
        startsAt < otherEnds && otherStarts < endsAt;

    /// <summary>
    /// Chooses the source shown at <paramref name="at"/>: -1 for the baseline, a grant index, or null for none.
    /// 1. A baseline restriction or unclear evidence always stands. 2. Baseline Active/Grace access wins.
    /// 3. Otherwise a grant covering <paramref name="at"/>. 4. Otherwise the next scheduled source, then the latest
    /// ended one. Ties: earlier start, baseline first, then input order (callers sort grants by term id).
    /// Selection ignores account suspension; <see cref="Evaluate"/> applies it to the chosen source afterwards.
    /// </summary>
    public static int? Select(SubscriptionAccessFacts? baseline, IReadOnlyList<SubscriptionAccessFacts> grants,
        DateTimeOffset at)
    {
        var candidates = new List<(int Index, SubscriptionAccessFacts Facts, SubscriptionAccessState State)>();
        if (baseline is not null)
            candidates.Add((-1, baseline, SubscriptionAccessPolicy.Evaluate(baseline, at).State));
        for (var index = 0; index < grants.Count; index++)
            candidates.Add((index, grants[index], SubscriptionAccessPolicy.Evaluate(grants[index], at).State));
        if (candidates.Count == 0) return null;
        if (baseline is not null && candidates[0].State is not (SubscriptionAccessState.Scheduled or SubscriptionAccessState.Expired))
            return -1;

        var covering = candidates.Where(item => item.Index >= 0 && item.Facts.StartsAt <= at && at < item.Facts.EndsAt)
            .OrderBy(item => item.State == SubscriptionAccessState.Active ? 0 : 1).ThenBy(item => item.Index).ToArray();
        if (covering.Length > 0) return covering[0].Index;
        var upcoming = candidates.Where(item => at < item.Facts.StartsAt)
            .OrderBy(item => item.Facts.StartsAt).ThenBy(item => item.Index).ToArray();
        if (upcoming.Length > 0) return upcoming[0].Index;
        return candidates.OrderByDescending(item => item.Facts.EndsAt).ThenBy(item => item.Index).First().Index;
    }

    /// <summary>The access evaluation for one source with the account's suspension intervals applied.</summary>
    public static SubscriptionAccessEvaluation Evaluate(SubscriptionAccessFacts facts, DateTimeOffset at,
        IReadOnlyList<SuspensionInterval>? suspensions = null) =>
        SubscriptionAccessPolicy.Evaluate(suspensions is null ? facts : facts with { Suspensions = suspensions }, at);
}
