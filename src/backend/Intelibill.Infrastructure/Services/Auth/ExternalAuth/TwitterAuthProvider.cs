using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Models;
using Intelibill.Domain.Enums;

namespace Intelibill.Infrastructure.Services.Auth.ExternalAuth;

internal sealed class TwitterAuthProvider(IHttpClientFactory httpClientFactory) : IExternalAuthProvider
{
    public ExternalAuthProvider Provider => ExternalAuthProvider.Twitter;

    public async Task<ErrorOr<ExternalUserInfo>> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        using var client = httpClientFactory.CreateClient(nameof(TwitterAuthProvider));

        // Use the caller's OAuth 2.0 user-context access token against the Twitter API v2.
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.twitter.com/2/users/me?user.fields=id,name,username");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return Error.Unauthorized("Auth.Twitter.InvalidToken", "The Twitter access token is invalid or has expired.");

        var result = await response.Content.ReadFromJsonAsync<TwitterUserResponse>(cancellationToken: cancellationToken);

        if (result?.Data is null)
            return Error.Unauthorized("Auth.Twitter.UserInfoError", "Failed to retrieve user information from Twitter.");

        var parts = (result.Data.Name ?? string.Empty).Split(' ', 2);

        return new ExternalUserInfo(
            ProviderKey: result.Data.Id,
            Email: null, // Twitter API v2 does not expose email without special approval.
            FirstName: parts.Length > 0 ? parts[0] : string.Empty,
            LastName: parts.Length > 1 ? parts[1] : string.Empty);
    }

    private sealed class TwitterUserResponse
    {
        [JsonPropertyName("data")] public TwitterUserData? Data { get; set; }
    }

    private sealed class TwitterUserData
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("username")] public string? Username { get; set; }
    }
}
