using Intelibill.Infrastructure.Options;
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
}
