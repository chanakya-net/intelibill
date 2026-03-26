using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Models;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Services.Auth.ExternalAuth;

internal sealed class FacebookAuthProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ExternalAuthOptions> options) : IExternalAuthProvider
{
    private readonly ExternalAuthOptions.FacebookOptions _fb = options.Value.Facebook;

    public ExternalAuthProvider Provider => ExternalAuthProvider.Facebook;

    public async Task<ErrorOr<ExternalUserInfo>> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(nameof(FacebookAuthProvider));

        // Inspect the token via the debug_token endpoint using our app token.
        var appToken = $"{_fb.AppId}|{_fb.AppSecret}";
        var debugUrl = $"https://graph.facebook.com/debug_token" +
                       $"?input_token={Uri.EscapeDataString(token)}" +
                       $"&access_token={Uri.EscapeDataString(appToken)}";

        var debug = await client.GetFromJsonAsync<FacebookDebugResponse>(debugUrl, cancellationToken);

        if (debug?.Data is null || !debug.Data.IsValid)
            return Error.Unauthorized("Auth.Facebook.InvalidToken", "The Facebook access token is invalid.");

        // Fetch user info using the user's own access token.
        var userUrl = $"https://graph.facebook.com/me" +
                      $"?fields=id,email,first_name,last_name" +
                      $"&access_token={Uri.EscapeDataString(token)}";

        var userInfo = await client.GetFromJsonAsync<FacebookUserResponse>(userUrl, cancellationToken);

        if (userInfo is null)
            return Error.Unauthorized("Auth.Facebook.UserInfoError", "Failed to retrieve user information from Facebook.");

        return new ExternalUserInfo(
            ProviderKey: userInfo.Id,
            Email: userInfo.Email,
            FirstName: userInfo.FirstName ?? string.Empty,
            LastName: userInfo.LastName ?? string.Empty);
    }

    private sealed class FacebookDebugResponse
    {
        [JsonPropertyName("data")] public FacebookTokenData? Data { get; set; }
    }

    private sealed class FacebookTokenData
    {
        [JsonPropertyName("is_valid")] public bool IsValid { get; set; }
    }

    private sealed class FacebookUserResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("first_name")] public string? FirstName { get; set; }
        [JsonPropertyName("last_name")] public string? LastName { get; set; }
    }
}
