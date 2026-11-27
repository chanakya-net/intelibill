namespace Intelibill.Licensing.Domain.Policies;

/// <summary>Frozen inputs of one prorated difference: two gross prices and one end-exclusive paid interval.</summary>
public sealed record ProrationInput(long PurchasedGrossPaise, long TargetGrossPaise, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, DateTimeOffset EvaluatedAt);

/// <summary>
/// The exact arithmetic of a proration: whole interval and remaining seconds, the signed gross difference and the
/// charge. <see cref="ChargePaise"/> is zero for a nonpositive difference.
/// </summary>
public sealed record ProrationResult(long TotalSeconds, long RemainingSeconds, long DifferencePaise, long ChargePaise);

/// <summary>
/// <c>proration/1</c>: the positive gross difference for the remaining fraction of the actual calendar interval
/// <c>[StartsAt, EndsAt)</c>, in integer paise and integer seconds with exactly one half-up rounding of the final
/// charge. The interval boundaries are whole UTC seconds; a subsecond evaluation instant counts only the whole
/// seconds still remaining (<c>floor((EndsAt - EvaluatedAt) / 1 s)</c>). The product is computed in 128 bits so the
/// largest catalogue price times the longest supported interval never overflows. No 30-day month is ever assumed.
/// </summary>
public static class ProrationPolicy
{
    public const string Version = "proration/1";

    public static ProrationResult? Prorate(ProrationInput input)
    {
        if (!Coherent(input))
            return null;
        var total = (input.EndsAt - input.StartsAt).Ticks / TimeSpan.TicksPerSecond;
        var remaining = (input.EndsAt - input.EvaluatedAt).Ticks / TimeSpan.TicksPerSecond;
        var difference = input.TargetGrossPaise - input.PurchasedGrossPaise;
        return new(total, remaining, difference, difference > 0 ? Charge(difference, remaining, total) : 0);
    }

    /// <summary>One half-up rounding: add a paisa when twice the remainder reaches the divisor.</summary>
    public static long Charge(long difference, long remaining, long total)
    {
        var (quotient, remainder) = Int128.DivRem((Int128)difference * remaining, total);
        var rounded = remainder * 2 >= total ? quotient + 1 : quotient;
        return rounded >= 0 && rounded <= difference ? (long)rounded
            : throw new ArgumentOutOfRangeException(nameof(difference));
    }

    private static bool Coherent(ProrationInput input) =>
        input.PurchasedGrossPaise is >= 0 and <= CatalogueDraftRules.MaxPaise &&
        input.TargetGrossPaise is >= 0 and <= CatalogueDraftRules.MaxPaise &&
        WholeSecond(input.StartsAt) && WholeSecond(input.EndsAt) && input.StartsAt < input.EndsAt &&
        input.StartsAt <= input.EvaluatedAt && input.EvaluatedAt < input.EndsAt;

    private static bool WholeSecond(DateTimeOffset instant) => instant.UtcTicks % TimeSpan.TicksPerSecond == 0;
}
