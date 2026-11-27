using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HsnFinder.Api.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace HsnFinder.Api.Tests;

public sealed class HsnEndpointTests
{
    private const string RiceJson =
        """{"name":"Basmati rice","HSN":"10063020","tax_scenarios":[{"condition":"Loose","tax_percentage":"0"}]}""";

    // PostConfigure runs after every configuration source, so a developer's local appsettings.secrets.json
    // or user secrets cannot change what these tests see.
    private static WebApplicationFactory<Program> CreateFactory(IChatClient chatClient, string aiModel = "test")
        => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.PostConfigure<AiSettings>(settings =>
                {
                    settings.ApiKey = "test";
                    settings.Model = aiModel;
                });
                services.PostConfigure<ApiKeySettings>(settings => settings.Key = "secret");

                services.RemoveAll<IChatClient>();
                services.AddSingleton(chatClient);

                services.RemoveAll<ResiliencePipeline>();
                services.AddSingleton(new ResiliencePipelineBuilder()
                    .AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 2, Delay = TimeSpan.Zero })
                    .Build());
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, bool withKey = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (withKey)
        {
            client.DefaultRequestHeaders.Add("X-Api-Key", "secret");
        }

        return client;
    }

    [Fact]
    public async Task Post_WithoutApiKeyHeader_Returns401()
    {
        await using var factory = CreateFactory(FakeChatClient.Returning(RiceJson));
        using var client = CreateClient(factory, withKey: false);

        var response = await client.PostAsJsonAsync("/api/hsn", new { productName = "rice" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_ValidRequest_Returns200WithEnvelope()
    {
        var fake = FakeChatClient.Returning(RiceJson);
        await using var factory = CreateFactory(fake);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/api/hsn", new { productName = "  Basmati rice " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("10063020", body.RootElement.GetProperty("data").GetProperty("hsn")[0].GetString());
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task Post_EmptyProductName_Returns400()
    {
        var fake = FakeChatClient.Returning(RiceJson);
        await using var factory = CreateFactory(fake);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/api/hsn", new { productName = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Post_ProviderFails_Returns503()
    {
        var fake = new FakeChatClient((_, _, _) => throw new InvalidOperationException("provider down"));
        await using var factory = CreateFactory(fake);
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync("/api/hsn", new { productName = "rice" });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("AI provider is currently unavailable.", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(3, fake.CallCount);
    }

    [Fact]
    public async Task Host_MissingAiModel_FailsToStart()
    {
        await using var factory = CreateFactory(FakeChatClient.Returning(RiceJson), aiModel: "");

        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        var validation = ex as OptionsValidationException
            ?? ex.InnerException as OptionsValidationException
            ?? (ex as AggregateException)?.InnerExceptions.OfType<OptionsValidationException>().FirstOrDefault();
        Assert.NotNull(validation);
        Assert.Contains("Ai:Model is required.", validation.Message);
    }
}
