using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Policies;
using Intelibill.Licensing.Domain.ValueObjects;

namespace Intelibill.Licensing.Application.Notifications;

public sealed record QueuedNoticeThreshold(
    string SourceKind, string SourceRevision, string Purpose, string Threshold, DateTimeOffset DueAt, DateTimeOffset Deadline);

public sealed record NoticeRelevance(string? Reason, PlannedThreshold? Current = null, DateTimeOffset? ValidUntil = null)
{
    public NoticeRelevance Until(DateTimeOffset? change) =>
        change < ValidUntil ? this with { ValidUntil = change } : this;
}

public static class ObsoleteNoticePolicy
{
    public static NoticeRelevance Evaluate(
        QueuedNoticeThreshold queued,
        TermPlanSource source,
        ShopPlanSource? shop,
        NoticeRestrictionScope restriction,
        DateTimeOffset now)
    {
        var term = source.Term;
        var facts = SubscriptionStatusProjection.ToFacts(term);
        if (facts.Evidence != TermEvidenceState.Complete || facts.Basis?.ToString() != queued.SourceKind || facts.RenewedAtEnd)
            return new("SourceNoLongerCurrent");
        if (shop is not null)
            facts = NotificationSchedulePolicy.OperationalFacts(facts, term.StartsAt, shop.CoverageFrom, shop.HandoffEndsAt);
        var cancellation = SubscriptionStatusProjection.ActiveCancellation(term)?.Version ?? 0;
        var revision = shop is null
            ? NotificationSchedulePolicy.BuildCommercialSourceRevision(
                queued.SourceKind, term.TermId, facts, source.TrialExtensionRevision, cancellation, term.PricePaise, term.Currency)
            : NotificationSchedulePolicy.BuildOperationalSourceRevision(
                queued.SourceKind, term.TermId, facts, source.TrialExtensionRevision, cancellation);
        if (revision != queued.SourceRevision)
            return new("SourceNoLongerCurrent");

        var schedule = NotificationSchedulePolicy.PlanThresholds(facts, facts.StartsAt, restriction, false);
        var candidate = schedule.SingleOrDefault(item => item.Purpose == queued.Purpose && item.Threshold == queued.Threshold
            && item.DueAt == queued.DueAt && item.EffectiveDeadlineAt == queued.Deadline);
        if (candidate is null)
            return new(restriction == NoticeRestrictionScope.Full ? "RestrictionChanged" : "SourceNoLongerCurrent");
        var until = candidate.Threshold == "GraceExpiry" ? DateTimeOffset.MaxValue : candidate.EffectiveDeadlineAt;
        if (schedule.FirstOrDefault(item => item.DueAt > candidate.DueAt) is { } next && next.DueAt < until)
            until = next.DueAt;
        return now < candidate.DueAt || now >= until ? new("WindowElapsed") : new(null, candidate, until);
    }
}
