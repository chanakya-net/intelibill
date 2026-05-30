using System.Security.Cryptography;
using System.Text;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Models;
using Intelibill.Domain.Enums;

namespace Intelibill.Infrastructure.Services.Auth.ExternalAuth;

internal sealed class ExternalOAuthFlowService(
    IEnumerable<IExternalOAuthCodeProvider> providers,
    IExternalOAuthStateStore stateStore) : IExternalOAuthFlowService
{
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    public async Task<ErrorOr<ExternalOAuthInitResult>> CreateAuthorizationUrlAsync(
        ExternalAuthProvider provider,
        CancellationToken cancellationToken = default)
    {
        var oauthProvider = providers.FirstOrDefault(p => p.Provider == provider);
        if (oauthProvider is null || !oauthProvider.IsEnabled)
            return Errors.Auth.UnsupportedProvider;

        var state = GenerateBase64UrlToken(32);
        var codeVerifier = oauthProvider.SupportsPkce ? GenerateBase64UrlToken(64) : null;
        var codeChallenge = codeVerifier is null ? null : ComputeCodeChallenge(codeVerifier);

        var urlResult = await oauthProvider.CreateAuthorizationUrlAsync(state, codeChallenge, cancellationToken);
        if (urlResult.IsError)
            return urlResult.Errors;

        await stateStore.StoreAsync(state, new ExternalOAuthState(provider, codeVerifier), StateTtl, cancellationToken);

        return new ExternalOAuthInitResult(urlResult.Value);
    }

    public async Task<ErrorOr<ExternalOAuthTokenResult>> ExchangeCodeAsync(
        string code,
        string state,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Errors.Auth.ExternalCodeMissing;

        if (string.IsNullOrWhiteSpace(state))
            return Errors.Auth.ExternalStateMissing;

        var stateData = await stateStore.ConsumeAsync(state, cancellationToken);
        if (stateData is null)
            return Errors.Auth.ExternalStateInvalid;

        var oauthProvider = providers.FirstOrDefault(p => p.Provider == stateData.Provider);
        if (oauthProvider is null || !oauthProvider.IsEnabled)
            return Errors.Auth.UnsupportedProvider;

        var tokenResult = await oauthProvider.ExchangeCodeForLoginTokenAsync(
            code,
            stateData.CodeVerifier,
            cancellationToken);

        if (tokenResult.IsError)
            return tokenResult.Errors;

        return new ExternalOAuthTokenResult(stateData.Provider, tokenResult.Value);
    }

    private static string GenerateBase64UrlToken(int bytesLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(bytesLength);
        return Base64UrlEncode(bytes);
    }

    private static string ComputeCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
