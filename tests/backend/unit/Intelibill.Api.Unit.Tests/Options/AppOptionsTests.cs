using System.ComponentModel.DataAnnotations;
using Intelibill.Api.Options;

namespace Intelibill.Api.Unit.Tests.Options;

public class AppOptionsTests
{
    [Fact]
    public void AppOptions_WhenBaseUrlMissing_FailsValidation()
    {
        var options = new AppOptions { BaseUrl = null! };
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(AppOptions.BaseUrl)));
    }

    [Fact]
    public void AppOptions_WhenBaseUrlProvided_PassesValidation()
    {
        var options = new AppOptions { BaseUrl = "https://inventory.test" };
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();

        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        Assert.True(isValid);
        Assert.Empty(results);
    }
}
