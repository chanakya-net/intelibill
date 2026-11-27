namespace Intelibill.Licensing.Domain.Policies;

/// <summary>Where the evaluated instant sits relative to the source term.</summary>
public enum CapacityChangeTiming { Current, Grace, AfterGrace, Trial, NotStarted }

/// <summary>What the frozen provider evidence says about a positive rounded charge.</summary>
public enum ProviderAmountDecision { Supported, BelowMinimum, Unsupported, Unavailable }

public sealed record CapacityChangeDecision(string Outcome, string? Reason, string[] Actions)
{
    public bool Payable => Outcome == CapacityChangeQuotePolicy.Payable;
}

/// <summary>
/// <c>capacity-change-quote/1</c>: a capacity increase is priced only inside the current paid interval and only as a
/// positive charge the selected provider can take in full. Everything else names its proper next step and carries no
/// payable amount: a nonpositive, rounded-zero or below-provider-minimum change is offered at renewal or as an explicit
/// administrator courtesy (never a fabricated order or minimum surcharge); grace needs an anchored renewal, after grace a
/// new term; a trial converts through an ordinary full paid purchase. Reductions are never quoted here.
/// </summary>
public static class CapacityChangeQuotePolicy
{
    public const string Version = "capacity-change-quote/1";
    public const string Payable = "Payable";
    public const string AtRenewalOrCourtesy = "AtRenewalOrCourtesy";
    public const string RenewalRequired = "RenewalRequired";
    public const string NewTermRequired = "NewTermRequired";
    public const string TrialConversion = "TrialConversion";
    public const string PaymentUnavailable = "PaymentUnavailable";

    /// <summary>Capacity and per-shop allowance may only stay or grow, and at least one of them must grow.</summary>
    public static bool IsIncrease(int priorCapacity, int targetCapacity, int priorAllowance, int targetAllowance) =>
        targetCapacity >= priorCapacity && targetAllowance >= priorAllowance &&
        (targetCapacity > priorCapacity || targetAllowance > priorAllowance);

    public static CapacityChangeTiming Timing(DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now,
        bool trial, bool graceEligible)
    {
        if (trial)
            return CapacityChangeTiming.Trial;
        if (now < startsAt)
            return CapacityChangeTiming.NotStarted;
        if (now < endsAt)
            return CapacityChangeTiming.Current;
        return graceEligible && now < endsAt + GracePolicy.EligiblePaidGrace
            ? CapacityChangeTiming.Grace : CapacityChangeTiming.AfterGrace;
    }

    /// <summary>The outcome outside the current interval, or null when the current interval must be prorated.</summary>
    public static CapacityChangeDecision? ForTiming(CapacityChangeTiming timing) => timing switch
    {
        CapacityChangeTiming.Trial => new(TrialConversion, "TrialSource", ["PurchasePaid"]),
        CapacityChangeTiming.Grace => new(RenewalRequired, "PaidThroughReached", ["Renew"]),
        CapacityChangeTiming.AfterGrace => new(NewTermRequired, "GraceEnded", ["PurchaseNewTerm"]),
        _ => null,
    };

    public static CapacityChangeDecision ForCharge(ProrationResult proration, ProviderAmountDecision provider)
    {
        if (proration.DifferencePaise <= 0)
            return Deferred("NonPositiveDifference");
        if (proration.ChargePaise == 0)
            return Deferred("RoundedToZero");
        return provider switch
        {
            ProviderAmountDecision.Supported => new(Payable, null, ["AcceptIncrease"]),
            ProviderAmountDecision.BelowMinimum => Deferred("BelowProviderMinimum"),
            ProviderAmountDecision.Unsupported => new(PaymentUnavailable, "AmountUnsupported", []),
            _ => new(PaymentUnavailable, "ProviderUnavailable", []),
        };
    }

    private static CapacityChangeDecision Deferred(string reason) =>
        new(AtRenewalOrCourtesy, reason, ["ScheduleAtRenewal", "RequestCourtesy"]);
}
