using Intelibill.Licensing.Domain.Policies;

namespace Intelibill.Licensing.Domain.ValueObjects;

/// <summary>Where a term's access comes from. Only <see cref="Paid"/> can ever be eligible for grace.</summary>
public enum SubscriptionBasis { Paid, Trial, Complimentary, Temporary, Transition }

/// <summary>How complete the stored evidence for a term is. Missing evidence never becomes invented access.</summary>
public enum TermEvidenceState { Complete, Incomplete, NeedsReview }

/// <summary>The evaluated access state. Expiry, incomplete evidence and verification outage are distinct.</summary>
public enum SubscriptionAccessState { PendingActivation, Scheduled, Active, Grace, Expired, Suspended, EvidenceIncomplete, NeedsReview }

/// <summary>A cancellation scheduled for <see cref="EffectiveAt"/>. Withdrawing it before then cancels it.</summary>
public sealed record ScheduledCancellation(DateTimeOffset EffectiveAt, DateTimeOffset? WithdrawnAt = null)
{
    public bool IsEffective => WithdrawnAt is not { } withdrawn || withdrawn >= EffectiveAt;
}

/// <summary>A suspension interval <c>[StartsAt, ReleasedAt)</c>. It overrides the state but never pauses time.</summary>
public sealed record SuspensionInterval(DateTimeOffset StartsAt, DateTimeOffset? ReleasedAt = null)
{
    public bool Covers(DateTimeOffset at) => StartsAt <= at && (ReleasedAt is not { } released || at < released);
}

/// <summary>
/// Immutable evaluation input for one coverage term. Dates and capacity come from the coverage row; basis,
/// anchor and allowance from the frozen term facts (absent for coverage-only rows). Lifecycle inputs
/// (cancellation, suspension) are supplied by their owning sources and default to none recorded.
/// </summary>
public sealed record SubscriptionAccessFacts(
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    TermEvidenceState Evidence,
    SubscriptionBasis? Basis = null,
    BillingAnchor? Anchor = null,
    int? PeriodOrdinal = null,
    ScheduledCancellation? Cancellation = null,
    IReadOnlyList<SuspensionInterval>? Suspensions = null);

/// <summary>The computed result. Every interval is end-exclusive; nothing is written back.</summary>
public sealed record SubscriptionAccessEvaluation(
    SubscriptionAccessState State,
    DateTimeOffset StartsAt,
    DateTimeOffset PaidThrough,
    DateTimeOffset? GraceEndsAt,
    DateTimeOffset? AccessEndsAt,
    DateTimeOffset? NextDeadline,
    DateTimeOffset? NextPeriodEndsAt);
