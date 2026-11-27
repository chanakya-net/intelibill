using HsnFinder.Api.Configuration;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HsnFinder.Api.Tests;

public sealed class AiSettingsValidationTests
{
    private const string SecretKey = "sk-super-secret-value";

    private static ServiceProvider Build(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddAiChatClient(configuration);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> Valid() => new()
    {
        ["Ai:ApiKey"] = SecretKey,
        ["Ai:Model"] = "m"
    };

    [Fact]
    public void ValidSettings_Resolve()
    {
        using var provider = Build(Valid());

        var settings = provider.GetRequiredService<IOptions<AiSettings>>().Value;

        Assert.Equal(SecretKey, settings.ApiKey);
        Assert.Equal("m", settings.Model);
        Assert.Equal("https://api.openai.com/v1", settings.Endpoint);
    }

    [Fact]
    public void MissingApiKey_Throws()
    {
        var values = Valid();
        values["Ai:ApiKey"] = "";
        using var provider = Build(values);

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AiSettings>>().Value);

        Assert.Contains("Ai:ApiKey is required.", ex.Message);
        Assert.DoesNotContain(SecretKey, ex.Message);
    }

    [Fact]
    public void MissingModel_Throws()
    {
        var values = Valid();
        values["Ai:Model"] = "";
        using var provider = Build(values);

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AiSettings>>().Value);

        Assert.Contains("Ai:Model is required.", ex.Message);
        Assert.DoesNotContain(SecretKey, ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("ftp://x")]
    public void InvalidEndpoint_Throws(string endpoint)
    {
        var values = Valid();
        values["Ai:Endpoint"] = endpoint;
        using var provider = Build(values);

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AiSettings>>().Value);

        Assert.Contains("Ai:Endpoint must be an absolute http(s) URL.", ex.Message);
    }

    [Fact]
    public void InvalidReasoningEffort_Throws()
    {
        var values = Valid();
        values["Ai:ReasoningEffort"] = "extreme";
        using var provider = Build(values);

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AiSettings>>().Value);

        Assert.Contains("Ai:ReasoningEffort must be one of:", ex.Message);
    }

    [Fact]
    public void ChatClient_IsRegisteredAsSingleton()
    {
        using var provider = Build(Valid());

        var first = provider.GetRequiredService<IChatClient>();
        var second = provider.GetRequiredService<IChatClient>();

        Assert.Same(first, second);
    }
}
