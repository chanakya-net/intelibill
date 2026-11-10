using System.Text.Json;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using ErrorOr;
using HsnFinder.Api.Configuration;
using HsnFinder.Api.Models.Agent;
using HsnFinder.Api.Models.Responses;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Polly;

namespace HsnFinder.Api.Services;

public sealed class HsnLookupService(
    IOptions<AzureAiSettings> azureSettings,
    IOptions<CacheSettings> cacheSettings,
    IMemoryCache cache,
    ResiliencePipeline resiliencePipeline,
    ILogger<HsnLookupService> logger) : IHsnLookupService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const int AgentResponsePreviewLength = 2000;

    private const string AgentPromptTemplate = """
Find the GST HSN code and the latest applicable tax percentage rules for the product "{0}" in India.

You must output the final result strictly as a valid JSON object. Do not include any conversational text, introductions, markdown code blocks (like ```json), or explanations outside of the JSON structure.

CRITICAL HSN FORMATTING RULE: Always provide the full, most granular HSN code available (typically an 8-digit code, e.g., "10063020"). Do not shorten it to a 2-digit or 4-digit heading unless a more specific code absolutely does not exist for the item.

If the product has different tax percentages based on packaging, branding, or conditions, you must break them out into an array under the "tax_scenarios" field, explaining the condition briefly within the structured schema.

If you identify multiple applicable full HSN codes, provide them as an array of strings; otherwise, provide a single string.

Use this exact JSON schema format:
{{
  "name": "product name",
  "HSN": "single_8_digit_hsn_code" or ["8_digit_hsn_1", "8_digit_hsn_2"],
  "tax_scenarios": [
    {{
      "condition": "Brief condition description (e.g., Pre-packaged and branded)",
      "tax_percentage": "percentage_value"
    }},
    {{
      "condition": "Brief condition description (e.g., Loose or unpackaged)",
      "tax_percentage": "percentage_value"
    }}
  ]
}}
""";

    public async Task<ErrorOr<HsnLookupResponse>> LookupAsync(string productName, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"hsn:{productName.ToLowerInvariant().Trim()}";

        if (cache.TryGetValue(cacheKey, out HsnLookupResponse? cached))
        {
            logger.LogInformation("Cache hit for product: {ProductName}", productName);
            return cached!;
        }

        try
        {
            var projectClient = new AIProjectClient(new Uri(azureSettings.Value.Endpoint), new DefaultAzureCredential());
            var agentRef = new AgentReference(name: azureSettings.Value.AgentName, version: azureSettings.Value.AgentVersion);
            var responseClient = projectClient.ProjectOpenAIClient.GetProjectResponsesClientForAgent(agentRef);

            var agentResponse = await resiliencePipeline.ExecuteAsync(
                ct =>
                {
                    ct.ThrowIfCancellationRequested();

                    var result = responseClient.CreateResponse(string.Format(AgentPromptTemplate, productName));
                    return ValueTask.FromResult(result.Value.GetOutputText());
                },
                cancellationToken);

            var responsePreview = agentResponse.Length > AgentResponsePreviewLength
                ? agentResponse[..AgentResponsePreviewLength]
                : agentResponse;

            logger.LogInformation(
                "Raw agent response preview for {ProductName}: {AgentResponsePreview}",
                productName,
                responsePreview);

            if (!TryExtractFirstJsonObject(agentResponse, out var jsonPayload))
            {
                logger.LogError(
                    "Failed to locate JSON object in agent response for {ProductName}. Raw response preview: {AgentResponsePreview}",
                    productName,
                    responsePreview);
                return Error.Unexpected("deserialization_failed", "Failed to parse agent response.");
            }

            if (!string.Equals(agentResponse, jsonPayload, StringComparison.Ordinal))
            {
                logger.LogWarning("Agent response for {ProductName} included non-JSON wrapper text; extracted JSON payload for parsing.", productName);
            }

            HsnAgentResult? agentResult;

            try
            {
                agentResult = JsonSerializer.Deserialize<HsnAgentResult>(jsonPayload, JsonSerializerOptions);
            }
            catch (JsonException ex)
            {
                logger.LogError(
                    ex,
                    "Failed to parse agent response for {ProductName}. Raw response preview: {AgentResponsePreview}. JSON payload preview: {JsonPayloadPreview}",
                    productName,
                    responsePreview,
                    BuildPreview(jsonPayload));
                return Error.Unexpected("deserialization_failed", "Failed to parse agent response.");
            }

            if (agentResult is null)
            {
                logger.LogError("Failed to parse agent response for {ProductName}", productName);
                return Error.Unexpected("deserialization_failed", "Failed to parse agent response.");
            }

            var response = new HsnLookupResponse(
                agentResult.Name,
                agentResult.Hsn,
                agentResult.TaxScenarios
                    .Select(taxScenario => new TaxScenario(taxScenario.Condition, taxScenario.TaxPercentage))
                    .ToArray());

            cache.Set(cacheKey, response, TimeSpan.FromDays(cacheSettings.Value.TtlDays));
            logger.LogInformation("Cached HSN result for {ProductName} with TTL {TtlDays} days", productName, cacheSettings.Value.TtlDays);

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "All Azure AI retries exhausted for {ProductName}: {Message}", productName, ex.Message);
            return Error.Failure("azure_ai_unavailable", "Azure AI Foundry is currently unavailable.");
        }
    }

    private static string BuildPreview(string value)
    {
        return value.Length > AgentResponsePreviewLength
            ? value[..AgentResponsePreviewLength]
            : value;
    }

    private static bool TryExtractFirstJsonObject(string input, out string jsonPayload)
    {
        jsonPayload = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var start = input.IndexOf('{');
        if (start < 0)
        {
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < input.Length; i++)
        {
            var ch = input[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
                continue;
            }

            if (ch == '}')
            {
                depth--;

                if (depth == 0)
                {
                    jsonPayload = input[start..(i + 1)];
                    return true;
                }
            }
        }

        return false;
    }
}
