namespace Intelibill.Licensing.Domain.Policies;

public static class TrialExtensionPolicy
{
    public const string Version = "trial-extension/1";
    public const int MaximumReasonLength = 500;
    public const int MaximumReviewReferenceLength = 80;

    public static bool Supports(DateTimeOffset end) =>
        end.UtcTicks % 10 == 0 && end < new DateTimeOffset(9999, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static bool ValidEnd(DateTimeOffset end, DateTimeOffset currentEnd, DateTimeOffset at) =>
        Supports(end) && end > currentEnd && end > at;
}
