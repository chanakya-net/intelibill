using System.ClientModel;
using HsnFinder.Api.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace HsnFinder.Api.Configuration;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiSettings>()
            .Bind(configuration.GetSection(AiSettings.SectionName))
            .Validate(settings => IsHttpUrl(settings.Endpoint), "Ai:Endpoint must be an absolute http(s) URL.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.ApiKey), "Ai:ApiKey is required.")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Model), "Ai:Model is required.")
            .Validate(
                settings => string.IsNullOrWhiteSpace(settings.ReasoningEffort)
                    || AiChatOptionsFactory.TryParseReasoningEffort(settings.ReasoningEffort, out _),
                $"Ai:ReasoningEffort must be one of: {string.Join(", ", Enum.GetNames<ReasoningEffort>())}.")
            .ValidateOnStart();

        services.AddChatClient(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<AiSettings>>().Value;
            var openAiClient = new OpenAIClient(
                new ApiKeyCredential(settings.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(settings.Endpoint) });

            return settings.Api == AiApi.Responses
                ? CreateResponsesChatClient(openAiClient, settings.Model)
                : openAiClient.GetChatClient(settings.Model).AsIChatClient();
        });

        return services;
    }

    // OpenAIClient.GetResponsesClient and AsIChatClient(ResponsesClient) are [Experimental("OPENAI001")].
#pragma warning disable OPENAI001
    private static IChatClient CreateResponsesChatClient(OpenAIClient openAiClient, string model)
        => openAiClient.GetResponsesClient().AsIChatClient(model);
#pragma warning restore OPENAI001

    private static bool IsHttpUrl(string? value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
