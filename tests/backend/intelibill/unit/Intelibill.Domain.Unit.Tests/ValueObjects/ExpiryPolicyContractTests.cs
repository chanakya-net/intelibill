using System.Text.Json;
using System.Text.Json.Serialization;
using Intelibill.Domain.ValueObjects;
namespace Intelibill.Domain.Unit.Tests.ValueObjects;

public sealed class ExpiryPolicyContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    [Fact]
    public void Evaluator_MatchesSharedOfflineDecisionCases()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "tests", "contracts"))) directory = directory.Parent;
        Assert.NotNull(directory);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "tests", "contracts", "expiry-policy-v1.json")));
        foreach (var row in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var actual = ExpiryPolicyEvaluator.Evaluate(row.GetProperty("batch").Deserialize<ExpiryBatchFacts>(Options)!, row.GetProperty("policy").Deserialize<ExpiryPolicy>(Options)!, row.GetProperty("catalogVersion").GetInt64(), row.GetProperty("businessDate").Deserialize<DateOnly>());
            var expected = row.GetProperty("expected");
            Assert.Equal(expected.GetProperty("allowed").GetBoolean(), actual.Allowed);
            Assert.Equal(expected.GetProperty("reasonCode").GetString(), actual.ReasonCode);
            Assert.Equal(expected.GetProperty("warning").GetBoolean(), actual.Warning);
            Assert.Equal(expected.GetProperty("requiresApproval").GetBoolean(), actual.RequiresApproval);
            Assert.Equal(expected.GetProperty("availableQuantity").GetDecimal(), actual.AvailableQuantity);
        }
    }
}
