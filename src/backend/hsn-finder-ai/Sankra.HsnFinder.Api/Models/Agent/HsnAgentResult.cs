using System.Text.Json.Serialization;

namespace HsnFinder.Api.Models.Agent;

internal sealed record HsnAgentResult
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("hsn")]
    [JsonConverter(typeof(HsnStringOrArrayConverter))]
    public string[] Hsn { get; init; } = [];

    [JsonPropertyName("tax_scenarios")]
    public AgentTaxScenario[] TaxScenarios { get; init; } = [];

    internal sealed record AgentTaxScenario
    {
        [JsonPropertyName("condition")]
        public string Condition { get; init; } = string.Empty;

        [JsonPropertyName("tax_percentage")]
        public string TaxPercentage { get; init; } = string.Empty;
    }
}
