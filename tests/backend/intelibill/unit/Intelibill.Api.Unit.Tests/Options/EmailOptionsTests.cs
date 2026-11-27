using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Intelibill.Api.Unit.Tests.Options;

public class EmailOptionsTests
{
    [Fact]
    public void Validator_AllowsBlankValuesWhenDisabled()
    {
        var options = new EmailOptions();
        var validator = new EmailOptionsValidator();

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        Assert.Same(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validator_RequiresSmtpSettingsWhenEnabled()
    {
        var options = new EmailOptions { Enabled = true };
        var validator = new EmailOptionsValidator();

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Email:Host", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("Email:Username", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("Email:Password", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("Email:FromEmail", StringComparison.Ordinal));
        Assert.Contains(result.Failures, failure => failure.Contains("Email:FromName", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_RejectsInvalidPortWhenEnabled()
    {
        var options = new EmailOptions { Enabled = true, Host = "smtp.office365.com", Port = 0, Username = "user", Password = "pass", FromEmail = "from@example.com", FromName = "Intelibill" };
        var validator = new EmailOptionsValidator();

        var result = validator.Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Email:Port", StringComparison.Ordinal));
    }

    private const string OutboxKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";

    private static EmailOptions Smtp() => new()
    {
        Enabled = true, Host = "smtp.example.com", Port = 587, Username = "user", Password = "smtp-secret-value",
        FromEmail = "from@example.com", FromName = "Intelibill",
    };

    private static EmailOptionsValidator Validator(string environment, string? baseUrl = "https://app.example.com", string? outboxKey = OutboxKey) =>
        new(new StubEnvironment(environment), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["App:BaseUrl"] = baseUrl,
            ["Identity:OutboxKey"] = outboxKey,
        }).Build());

    [Fact]
    public void Production_DisabledMail_FailsValidation()
    {
        var result = Validator(Environments.Production).Validate(Microsoft.Extensions.Options.Options.DefaultName, new EmailOptions());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Email:Enabled", StringComparison.Ordinal));
    }

    [Fact]
    public void Development_DisabledMail_IsAllowed()
    {
        var result = Validator(Environments.Development).Validate(Microsoft.Extensions.Options.Options.DefaultName, new EmailOptions());

        Assert.Same(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Production_ConfiguredMail_Passes()
    {
        var result = Validator(Environments.Production).Validate(Microsoft.Extensions.Options.Options.DefaultName, Smtp());

        Assert.Same(ValidateOptionsResult.Success, result);
    }

    [Theory]
    [InlineData("http://app.example.com")]
    [InlineData("/relative")]
    [InlineData(null)]
    public void Production_NonHttpsBaseUrl_FailsValidation(string? baseUrl)
    {
        var result = Validator(Environments.Production, baseUrl: baseUrl).Validate(Microsoft.Extensions.Options.Options.DefaultName, Smtp());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("App:BaseUrl", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-base64")]
    [InlineData("c2hvcnQ=")]
    public void Production_MissingOutboxKey_FailsValidation(string? outboxKey)
    {
        var result = Validator(Environments.Production, outboxKey: outboxKey).Validate(Microsoft.Extensions.Options.Options.DefaultName, Smtp());

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Identity:OutboxKey", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_Failures_NeverContainSecretValues()
    {
        var result = Validator(Environments.Production, baseUrl: "http://insecure.example.com", outboxKey: "bad-key-value")
            .Validate(Microsoft.Extensions.Options.Options.DefaultName, Smtp());

        Assert.True(result.Failed);
        Assert.DoesNotContain(result.Failures, failure => failure.Contains("smtp-secret-value", StringComparison.Ordinal)
            || failure.Contains("bad-key-value", StringComparison.Ordinal)
            || failure.Contains("insecure.example.com", StringComparison.Ordinal));
    }

    [Fact]
    public void Production_LocalCapture_FailsValidation()
    {
        var options = new EmailOptions { Enabled = true, LocalCapture = true, Host = "localhost", FromEmail = "from@example.com", FromName = "Intelibill" };

        var result = Validator(Environments.Production).Validate(Microsoft.Extensions.Options.Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Email:LocalCapture", StringComparison.Ordinal));
    }

    private sealed class StubEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Intelibill.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
