using System.Globalization;
using System.Text.Json;
using Intelibill.Licensing.Domain.Policies;

namespace Intelibill.Licensing.Integration.Tests;

public sealed partial class P0601Tests
{
    /// <summary>
    /// The committed proration-v1 corpus is fed verbatim to the policy: calendar boundaries, exact and near halves,
    /// subsecond evaluation, the largest price over a 366-day term and every nonpayable outcome.
    /// </summary>
    [Fact]
    public void ProrationV1VectorsMatchPolicy()
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(ProjectPaths.Root, "tests/contracts/licensing/proration-v1.json")));
        var root = corpus.RootElement;
        Assert.Equal(ProrationPolicy.Version, root.GetProperty("policy").GetString());
        Assert.Equal(CapacityChangeQuotePolicy.Version, root.GetProperty("quotePolicy").GetString());
        var vectors = root.GetProperty("vectors").EnumerateArray().ToArray();
        Assert.True(vectors.Length >= 19, "The corpus must keep every boundary vector.");
        foreach (var vector in vectors)
            AssertVector(vector);
    }

    private static void AssertVector(JsonElement vector)
    {
        var name = vector.GetProperty("name").GetString();
        var result = ProrationPolicy.Prorate(new(vector.GetProperty("purchasedGrossPaise").GetInt64(),
            vector.GetProperty("targetGrossPaise").GetInt64(), Instant(vector, "startsAt"), Instant(vector, "endsAt"),
            Instant(vector, "evaluatedAt")));
        if (vector.TryGetProperty("invalid", out _))
        {
            Assert.True(result is null, name);
            return;
        }
        var expected = vector.GetProperty("expected");
        Assert.True(result is not null, name);
        Assert.Equal((name, expected.GetProperty("totalSeconds").GetInt64()), (name, result.TotalSeconds));
        Assert.Equal((name, expected.GetProperty("remainingSeconds").GetInt64()), (name, result.RemainingSeconds));
        Assert.Equal((name, expected.GetProperty("differencePaise").GetInt64()), (name, result.DifferencePaise));
        Assert.Equal((name, expected.GetProperty("chargePaise").GetInt64()), (name, result.ChargePaise));
        var minimum = vector.TryGetProperty("providerMinimumPaise", out var floor) ? floor.GetInt64() : (long?)null;
        var provider = minimum is { } min && result.ChargePaise < min
            ? ProviderAmountDecision.BelowMinimum : ProviderAmountDecision.Supported;
        var decision = CapacityChangeQuotePolicy.ForCharge(result, provider);
        Assert.Equal((name, expected.GetProperty("outcome").GetString()), (name, decision.Outcome));
        Assert.Equal((name, expected.GetProperty("reason").GetString()), (name, decision.Reason));
        Assert.Equal((name, decision.Payable), (name, decision.Outcome == "Payable"));
    }

    private static DateTimeOffset Instant(JsonElement vector, string property) => DateTimeOffset.Parse(
        vector.GetProperty(property).GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
}
