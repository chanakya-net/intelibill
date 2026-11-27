using HsnFinder.Api.Configuration;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace HsnFinder.Api.Services;

public static class AiChatOptionsFactory
{
    // Keys Microsoft.Extensions.AI.OpenAI reads from HostedWebSearchTool.AdditionalProperties
    // when it maps the tool to an OpenAI Responses web_search tool.
    private const string UserLocationProperty = "UserLocation";
    private const string SearchContextSizeProperty = "SearchContextSize";

    public static ChatOptions Create(AiSettings settings)
    {
        var options = new ChatOptions();

        if (TryParseReasoningEffort(settings.ReasoningEffort, out var effort))
        {
            options.Reasoning = new ReasoningOptions { Effort = effort };
        }

        if (settings.WebSearch.Enabled)
        {
            options.Tools = [CreateWebSearchTool(settings)];
        }

        if (settings.Api == AiApi.Responses && !string.IsNullOrWhiteSpace(settings.Verbosity))
        {
            var verbosity = settings.Verbosity;
            options.RawRepresentationFactory = _ => CreateResponsesOptions(verbosity);
        }

        return options;
    }

    internal static bool TryParseReasoningEffort(string? value, out ReasoningEffort effort)
    {
        effort = default;
        var name = Enum.GetNames<ReasoningEffort>()
            .FirstOrDefault(candidate => string.Equals(candidate, value?.Trim(), StringComparison.OrdinalIgnoreCase));

        return name is not null && Enum.TryParse(name, out effort);
    }

    private static HostedWebSearchTool CreateWebSearchTool(AiSettings settings)
    {
        if (settings.Api != AiApi.Responses)
        {
            return new HostedWebSearchTool();
        }

        var properties = new Dictionary<string, object?>();
        var webSearch = settings.WebSearch;

        // WebSearchToolLocation and WebSearchToolContextSize are [Experimental("OPENAI001")] in the OpenAI SDK;
        // they are the only typed way to send web_search user_location and search_context_size.
#pragma warning disable OPENAI001
        if (!string.IsNullOrWhiteSpace(webSearch.Country))
        {
            properties[UserLocationProperty] = WebSearchToolLocation.CreateApproximateLocation(country: webSearch.Country);
        }

        if (!string.IsNullOrWhiteSpace(webSearch.ContextSize))
        {
            properties[SearchContextSizeProperty] = new WebSearchToolContextSize(webSearch.ContextSize);
        }
#pragma warning restore OPENAI001

        return new HostedWebSearchTool(properties);
    }

    // CreateResponseOptions and ResponseTextOptions are [Experimental("OPENAI001")], and JsonPatch is
    // [Experimental("SCME0001")]. OpenAI 2.15.0 has no typed text.verbosity member, so it is set through the
    // SDK's JsonPatch extension point on the text options.
#pragma warning disable OPENAI001, SCME0001
    private static CreateResponseOptions CreateResponsesOptions(string verbosity)
    {
        var textOptions = new ResponseTextOptions();
        textOptions.Patch.Set("$.verbosity"u8, verbosity);

        return new CreateResponseOptions { TextOptions = textOptions };
    }
#pragma warning restore OPENAI001, SCME0001
}
