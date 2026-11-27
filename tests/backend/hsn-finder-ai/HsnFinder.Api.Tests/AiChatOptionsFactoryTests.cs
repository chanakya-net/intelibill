// The OpenAI Responses types asserted on here (WebSearchToolApproximateLocation, WebSearchToolContextSize)
// are marked [Experimental("OPENAI001")] in the OpenAI SDK.
#pragma warning disable OPENAI001
using System.Text.Json.Nodes;
using HsnFinder.Api.Configuration;
using HsnFinder.Api.Services;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace HsnFinder.Api.Tests;

public sealed class AiChatOptionsFactoryTests
{
    [Fact]
    public void Create_WebSearchEnabled_AddsHostedWebSearchTool()
    {
        var settings = CreateSettings();
        settings.WebSearch.Enabled = true;

        var options = AiChatOptionsFactory.Create(settings);

        var tool = Assert.Single(options.Tools!);
        Assert.IsType<HostedWebSearchTool>(tool);
    }

    [Fact]
    public void Create_WebSearchDisabled_AddsNoTools()
    {
        var settings = CreateSettings();
        settings.WebSearch.Enabled = false;

        var options = AiChatOptionsFactory.Create(settings);

        Assert.True(options.Tools is null || options.Tools.Count == 0);
    }

    [Fact]
    public void Create_ChatCompletionsApi_SendsNoResponsesOnlySettings()
    {
        var settings = CreateSettings();
        settings.Api = AiApi.ChatCompletions;
        settings.Verbosity = "low";
        settings.WebSearch = new AiWebSearchSettings { Enabled = true, Country = "IN", ContextSize = "low" };

        var options = AiChatOptionsFactory.Create(settings);

        Assert.Null(options.RawRepresentationFactory);
        var tool = Assert.Single(options.Tools!);
        Assert.Empty(tool.AdditionalProperties);
    }

    [Fact]
    public void Create_ReasoningEffortLow_IsCarried()
    {
        var settings = CreateSettings();
        settings.ReasoningEffort = "low";

        var options = AiChatOptionsFactory.Create(settings);

        Assert.Equal(ReasoningEffort.Low, options.Reasoning?.Effort);
    }

    [Fact]
    public void Create_NoReasoningEffort_LeavesItUnset()
    {
        var settings = CreateSettings();
        settings.ReasoningEffort = null;

        var options = AiChatOptionsFactory.Create(settings);

        Assert.Null(options.Reasoning);
    }

    [Fact]
    public void Create_CountryAndContextSize_AreCarried()
    {
        var settings = CreateSettings();
        settings.WebSearch = new AiWebSearchSettings { Enabled = true, Country = "IN", ContextSize = "low" };

        var options = AiChatOptionsFactory.Create(settings);

        var tool = Assert.Single(options.Tools!);
        var location = Assert.IsType<WebSearchToolApproximateLocation>(tool.AdditionalProperties["UserLocation"]);
        Assert.Equal("IN", location.Country);
        Assert.Equal(WebSearchToolContextSize.Low, Assert.IsType<WebSearchToolContextSize>(tool.AdditionalProperties["SearchContextSize"]));
    }

    [Fact]
    public async Task Create_ResponsesApi_ProducesTargetRequestBody()
    {
        var settings = CreateSettings();
        settings.ReasoningEffort = "low";
        settings.Verbosity = "low";
        settings.WebSearch = new AiWebSearchSettings { Enabled = true, Country = "IN", ContextSize = "low" };
        var handler = new StubResponsesHandler();
        using var chatClient = handler.CreateResponsesChatClient();

        var response = await chatClient.GetResponseAsync("Find HSN for rice", AiChatOptionsFactory.Create(settings));

        Assert.Equal("ok", response.Text);
        Assert.Equal("https://ai.example.test/v1/responses", handler.RequestUri?.ToString());
        var body = JsonNode.Parse(handler.RequestBody!)!;
        Assert.Equal("test-model", body["model"]!.GetValue<string>());
        Assert.Equal("low", body["reasoning"]!["effort"]!.GetValue<string>());
        Assert.Equal("low", body["text"]!["verbosity"]!.GetValue<string>());
        var tool = Assert.Single(body["tools"]!.AsArray())!;
        Assert.Equal("web_search", tool["type"]!.GetValue<string>());
        Assert.Equal("low", tool["search_context_size"]!.GetValue<string>());
        Assert.Equal("approximate", tool["user_location"]!["type"]!.GetValue<string>());
        Assert.Equal("IN", tool["user_location"]!["country"]!.GetValue<string>());
    }

    private static AiSettings CreateSettings() => new()
    {
        Endpoint = "https://ai.example.test/v1",
        ApiKey = "test-key",
        Model = "test-model"
    };
}
