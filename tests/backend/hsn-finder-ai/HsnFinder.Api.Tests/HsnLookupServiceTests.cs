using HsnFinder.Api.Configuration;
using HsnFinder.Api.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HsnFinder.Api.Tests;

public sealed class HsnLookupServiceTests : IDisposable
{
    private const string RiceJson =
        """{"name":"Basmati rice","HSN":"10063020","tax_scenarios":[{"condition":"Loose","tax_percentage":"0"}]}""";

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    [Fact]
    public async Task Lookup_PlainJson_ReturnsMappedResponse()
    {
        var sut = CreateService(FakeChatClient.Returning(RiceJson));

        var result = await sut.LookupAsync("Basmati rice");

        Assert.False(result.IsError);
        Assert.Equal("Basmati rice", result.Value.Name);
        Assert.Equal(["10063020"], result.Value.Hsn);
        var scenario = Assert.Single(result.Value.TaxScenarios);
        Assert.Equal("Loose", scenario.Condition);
        Assert.Equal("0", scenario.TaxPercentage);
    }

    [Fact]
    public async Task Lookup_JsonWrappedInProseAndCitations_IsParsed()
    {
        var answer = "Here you go:\n" + RiceJson + " ([cbic-gst.gov.in](https://cbic-gst.gov.in/x?utm_source=openai))";
        var sut = CreateService(FakeChatClient.Returning(answer));

        var result = await sut.LookupAsync("Basmati rice");

        Assert.False(result.IsError);
        Assert.Equal("Basmati rice", result.Value.Name);
        Assert.Equal(["10063020"], result.Value.Hsn);
    }

    [Fact]
    public async Task Lookup_HsnArray_ReturnsAllCodes()
    {
        var json = """{"name":"Rice","HSN":["10063020","10063090"],"tax_scenarios":[]}""";
        var sut = CreateService(FakeChatClient.Returning(json));

        var result = await sut.LookupAsync("Rice");

        Assert.False(result.IsError);
        Assert.Equal(["10063020", "10063090"], result.Value.Hsn);
    }

    [Fact]
    public async Task Lookup_SecondCallSameProductDifferentCase_UsesCache()
    {
        var chatClient = FakeChatClient.Returning(RiceJson);
        var sut = CreateService(chatClient);

        var first = await sut.LookupAsync("Rice");
        var second = await sut.LookupAsync(" rice ");

        Assert.False(first.IsError);
        Assert.False(second.IsError);
        Assert.Equal(1, chatClient.CallCount);
    }

    [Fact]
    public async Task Lookup_SendsProductNameAndConfiguredOptions()
    {
        var chatClient = FakeChatClient.Returning(RiceJson);
        var settings = CreateAiSettings();
        settings.WebSearch.Enabled = true;
        var sut = CreateService(chatClient, settings);

        await sut.LookupAsync("Basmati rice");

        Assert.Contains("Basmati rice", chatClient.LastPrompt);
        Assert.NotNull(chatClient.LastOptions);
        Assert.IsType<HostedWebSearchTool>(Assert.Single(chatClient.LastOptions.Tools!));
    }

    [Fact]
    public async Task Lookup_NoJsonInAnswer_ReturnsDeserializationFailed()
    {
        var chatClient = FakeChatClient.Returning("I could not find that product.");
        var sut = CreateService(chatClient);

        var first = await sut.LookupAsync("Unknown thing");
        var second = await sut.LookupAsync("Unknown thing");

        Assert.True(first.IsError);
        Assert.Equal("deserialization_failed", first.FirstError.Code);
        Assert.True(second.IsError);
        Assert.Equal(2, chatClient.CallCount);
    }

    [Fact]
    public async Task Lookup_ProviderAlwaysThrows_RetriesThenReturnsUnavailable()
    {
        var chatClient = new FakeChatClient((_, _, _) => throw new HttpRequestException("boom"));
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 2, Delay = TimeSpan.Zero })
            .Build();
        var sut = CreateService(chatClient, pipeline: pipeline);

        var result = await sut.LookupAsync("Rice");

        Assert.Equal(3, chatClient.CallCount);
        Assert.True(result.IsError);
        Assert.Equal("ai_provider_unavailable", result.FirstError.Code);
        Assert.Equal("AI provider is currently unavailable.", result.FirstError.Description);
    }

    [Fact]
    public async Task Lookup_Cancelled_PropagatesCancellation()
    {
        var chatClient = new FakeChatClient((_, _, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, RiceJson)));
        });
        var sut = CreateService(chatClient);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sut.LookupAsync("Rice", cts.Token));
        Assert.True(chatClient.CallCount <= 1);
    }

    private HsnLookupService CreateService(
        IChatClient chatClient,
        AiSettings? aiSettings = null,
        ResiliencePipeline? pipeline = null)
        => new(
            chatClient,
            Options.Create(aiSettings ?? CreateAiSettings()),
            Options.Create(new CacheSettings { TtlDays = 30 }),
            _cache,
            pipeline ?? ResiliencePipeline.Empty,
            NullLogger<HsnLookupService>.Instance);

    private static AiSettings CreateAiSettings() => new()
    {
        Endpoint = "https://ai.example.test/v1",
        ApiKey = "test-key",
        Model = "test-model"
    };
}
