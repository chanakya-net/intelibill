using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Google.Apis.Auth;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Models;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace Intelibill.Infrastructure.Services.Auth.ExternalAuth;

internal sealed class GoogleAuthProvider(
    IOptions<ExternalAuthOptions> options,
    IHttpClientFactory httpClientFactory) : IExternalAuthProvider, IExternalOAuthCodeProvider
{
    private readonly ExternalAuthOptions.GoogleOptions _google = options.Value.Google;

    public ExternalAuthProvider Provider => ExternalAuthProvider.Google;
    public bool IsEnabled => _google.Enabled;
    public bool SupportsPkce => true;

    public Task<ErrorOr<string>> CreateAuthorizationUrlAsync(
        string state,
        string? codeChallenge,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return Task.FromResult<ErrorOr<string>>(Errors.Auth.UnsupportedProvider);

        var url = $"{_google.AuthorizationEndpoint}" +
                  $"?response_type=code" +
                  $"&client_id={Uri.EscapeDataString(_google.ClientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(_google.RedirectUri)}" +
                  $"&scope={Uri.EscapeDataString(_google.Scope)}" +
                  $"&state={Uri.EscapeDataString(state)}" +
                  $"&code_challenge={Uri.EscapeDataString(codeChallenge ?? string.Empty)}" +
                  "&code_challenge_method=S256";

        return Task.FromResult<ErrorOr<string>>(url);
    }

    public async Task<ErrorOr<string>> ExchangeCodeForLoginTokenAsync(
        string code,
        string? codeVerifier,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return Errors.Auth.UnsupportedProvider;

        if (string.IsNullOrWhiteSpace(codeVerifier))
            return Errors.Auth.ExternalProviderError("Google PKCE verifier is missing.");

        using var client = httpClientFactory.CreateClient(nameof(GoogleAuthProvider));
        using var request = new HttpRequestMessage(HttpMethod.Post, _google.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _google.ClientId,
                ["client_secret"] = _google.ClientSecret,
                ["code"] = code,
                ["redirect_uri"] = _google.RedirectUri,
                ["grant_type"] = "authorization_code",
                ["code_verifier"] = codeVerifier,
            }),
        };

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return Errors.Auth.ExternalProviderError("Google code exchange failed.");

        var token = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken);
        if (token is null || string.IsNullOrWhiteSpace(token.IdToken))
            return Errors.Auth.ExternalProviderError("Google did not return an identity token.");

        return token.IdToken;
    }

    public async Task<ErrorOr<ExternalUserInfo>> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_google.ClientId))
            return Errors.Auth.ExternalProviderError("Google authentication is not configured.");

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = [_google.ClientId],
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(token, settings);

            var parts = (payload.Name ?? string.Empty).Split(' ', 2);
            var firstName = parts.Length > 0 ? parts[0] : string.Empty;
            var lastName = parts.Length > 1 ? parts[1] : string.Empty;

            return new ExternalUserInfo(
                ProviderKey: payload.Subject,
                Email: payload.Email,
                FirstName: firstName,
                LastName: lastName);
        }
        catch (InvalidJwtException ex)
        {
            return Error.Unauthorized("Auth.Google.InvalidToken", ex.Message);
        }
    }

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
    }
}
