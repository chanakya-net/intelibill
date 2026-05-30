using ErrorOr;
using Intelibill.Domain.Enums;

namespace Intelibill.Infrastructure.Services.Auth.ExternalAuth;

internal interface IExternalOAuthCodeProvider
{
    ExternalAuthProvider Provider { get; }
    bool IsEnabled { get; }
    bool SupportsPkce { get; }

    Task<ErrorOr<string>> CreateAuthorizationUrlAsync(
        string state,
        string? codeChallenge,
        CancellationToken cancellationToken = default);

    Task<ErrorOr<string>> ExchangeCodeForLoginTokenAsync(
        string code,
        string? codeVerifier,
        CancellationToken cancellationToken = default);
}
