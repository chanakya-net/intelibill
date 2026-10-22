using System.Net;
using System.Text;
using Intelibill.Application.Common.Models;
using Intelibill.Infrastructure.Options;
using Intelibill.Infrastructure.Services.Auth.ExternalAuth;
using Microsoft.Extensions.Options;
using NSubstitute;
namespace Intelibill.Api.Unit.Tests.Infrastructure;

public sealed class ExternalProviderTrustTests
{
    [Theory]
    [InlineData("owner@gmail.com", true, null, ExternalEmailTrust.Verified)]
    [InlineData("owner@company.com", true, "company.com", ExternalEmailTrust.Verified)]
    [InlineData("owner@thirdparty.com", true, null, ExternalEmailTrust.Unverified)]
    [InlineData("owner@gmail.com", false, null, ExternalEmailTrust.Unverified)]
    public void Google_OnlyAuthoritativeEmailEstablishesRecovery(string email, bool verified, string? domain, ExternalEmailTrust expected) =>
        Assert.Equal(expected, GoogleAuthProvider.ClassifyEmailTrust(email, verified, domain));

    [Theory]
    [InlineData("other-app", "subject", 3600)]
    [InlineData("app", "other-subject", 3600)]
    [InlineData("app", "subject", -3600)]
    public async Task Facebook_WrongAppOrSubjectOrExpiry_IsRejected(string appId, string subject, int expiryOffset)
    {
        var expiry = DateTimeOffset.UtcNow.AddSeconds(expiryOffset).ToUnixTimeSeconds();
        using var client = new HttpClient(new FacebookHandler(appId, subject, expiry));
        var factory = Substitute.For<IHttpClientFactory>(); factory.CreateClient(Arg.Any<string>()).Returns(client);
        var options = Microsoft.Extensions.Options.Options.Create(new ExternalAuthOptions { Facebook = new ExternalAuthOptions.FacebookOptions { Enabled = true, AppId = "app", AppSecret = "secret" } });
        var provider = new FacebookAuthProvider(factory, options);
        var result = await provider.ValidateTokenAsync("opaque-test-token");
        Assert.True(result.IsError);
    }
    [Theory]
    [InlineData("https://graph.facebook.com/debug_token?input_token=BEARER_SENTINEL")]
    [InlineData("https://graph.facebook.com/me?access_token=BEARER_SENTINEL")]
    [InlineData("https://graph.facebook.com/oauth/access_token?client_secret=SECRET_SENTINEL")]
    public void ProviderSecretUrls_AreExcludedFromExportedHttpTraces(string url) =>
        Assert.True(Intelibill.Infrastructure.Extensions.OpenTelemetryServiceCollectionExtensions.ContainsIdentitySecret(new Uri(url)));

    private sealed class FacebookHandler(string appId, string subject, long expiry) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var payload = request.RequestUri!.AbsolutePath.EndsWith("debug_token", StringComparison.Ordinal)
                ? System.Text.Json.JsonSerializer.Serialize(new { data = new { is_valid = true, app_id = appId, user_id = subject, expires_at = expiry, data_access_expires_at = expiry } })
                : "{\"id\":\"subject\",\"email\":\"owner@test.com\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "application/json") });
        }
    }
}
