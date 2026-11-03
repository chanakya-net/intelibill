using System.Globalization;
using System.Text;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public sealed record OfflineArithmeticMismatch(string Path, decimal? Supplied, decimal? Expected, string Code);

internal static class OfflineArithmeticMismatchCodes
{
    internal const string AmountMismatch = "amount_mismatch";
    internal const string DiscountExceedsBasis = "discount_exceeds_basis";
    internal const string ScaleExceeded = "scale_exceeded";
    internal const string UnsupportedValue = "unsupported_value";
    internal const string RuleEvidenceMissing = "rule_evidence_missing";
    internal const string RuleThresholdNotMet = "rule_threshold_not_met";
    internal const string UnsupportedVersion = "unsupported_version";
}

internal sealed record OfflineArithmeticVerdict(string Contract, IReadOnlyList<OfflineArithmeticMismatch> Mismatches)
{
    internal const string LegacyContract = "legacy";
    internal const string Version1Contract = "v1";
    internal const string UnsupportedContract = "unsupported";

    internal bool IsConsistent => Mismatches.Count == 0;

    internal bool IsUnsupportedVersion => Contract == UnsupportedContract;

    // A bounded, amount-only summary for the review issue; the full list travels in the sync response.
    internal string Describe(int maxLength)
    {
        var builder = new StringBuilder($"Offline invoice arithmetic ({Contract}) does not reconcile:");
        for (var i = 0; i < Mismatches.Count; i++)
        {
            var entry = $" {Mismatches[i].Path} supplied {Format(Mismatches[i].Supplied)} expected {Format(Mismatches[i].Expected)} ({Mismatches[i].Code});";
            var suffix = i + 1 < Mismatches.Count ? $" +{Mismatches.Count - i - 1} more" : string.Empty;
            if (builder.Length + entry.Length + suffix.Length > maxLength)
            {
                builder.Append(CultureInfo.InvariantCulture, $" +{Mismatches.Count - i} more");
                break;
            }

            builder.Append(entry);
        }

        return builder.Length <= maxLength ? builder.ToString() : builder.ToString(0, maxLength);
    }

    private static string Format(decimal? value) =>
        value?.ToString("0.00##", CultureInfo.InvariantCulture) ?? "none";
}
