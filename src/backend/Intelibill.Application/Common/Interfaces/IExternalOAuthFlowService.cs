using ErrorOr;
using Intelibill.Application.Common.Models;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Common.Interfaces;

public interface IExternalOAuthFlowService
{
    Task<ErrorOr<ExternalOAuthInitResult>> CreateAuthorizationUrlAsync(
        ExternalAuthProvider provider,
        CancellationToken cancellationToken = default);

    Task<ErrorOr<ExternalOAuthTokenResult>> ExchangeCodeAsync(
        string code,
        string state,
        CancellationToken cancellationToken = default);
}
