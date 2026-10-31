using Intelibill.Infrastructure.Services.Auth;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Intelibill.Api.Unit.Tests.Services.Auth;

public class MailKitSmtpClientFactoryTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Preview")]
    public async Task EveryOtherEnvironment_KeepsCertificateRevocationChecking(string environmentName)
    {
        var factory = new MailKitSmtpClientFactory(EnvironmentNamed(environmentName));

        Assert.True(factory.ChecksCertificateRevocation);
        await using var client = factory.Create();
        Assert.True(Assert.IsType<MailKitSmtpClientFactory.MailKitSmtpClientAdapter>(client).ChecksCertificateRevocation);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task DevelopmentAndTesting_SkipCertificateRevocationChecking(string environmentName)
    {
        var factory = new MailKitSmtpClientFactory(EnvironmentNamed(environmentName));

        Assert.False(factory.ChecksCertificateRevocation);
        await using var client = factory.Create();
        Assert.False(Assert.IsType<MailKitSmtpClientFactory.MailKitSmtpClientAdapter>(client).ChecksCertificateRevocation);
    }

    private static IHostEnvironment EnvironmentNamed(string environmentName)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return environment;
    }
}
