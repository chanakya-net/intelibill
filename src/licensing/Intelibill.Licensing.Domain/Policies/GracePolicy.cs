using Intelibill.Licensing.Domain.ValueObjects;

namespace Intelibill.Licensing.Domain.Policies;

/// <summary>
/// Seven elapsed days of grace after an eligible paid term, <c>[paidThrough, paidThrough + 7 days)</c>, fixed by
/// the original boundary. Trial, courtesy, temporary and transition access, an effective scheduled cancellation
/// at or before the boundary and incomplete evidence never receive grace. Nothing is scheduled or written.
/// </summary>
public static class GracePolicy
{
    public const string Version = "access-grace/1";
    public static readonly TimeSpan EligiblePaidGrace = TimeSpan.FromDays(7);

    public static DateTimeOffset? GraceEndsAt(SubscriptionAccessFacts facts) =>
        IsEligible(facts) ? facts.EndsAt + EligiblePaidGrace : null;

    public static bool IsEligible(SubscriptionAccessFacts facts) =>
        facts.Evidence == TermEvidenceState.Complete && facts.Basis == SubscriptionBasis.Paid
        && !(facts.Cancellation is { IsEffective: true } cancellation && cancellation.EffectiveAt <= facts.EndsAt);
}
