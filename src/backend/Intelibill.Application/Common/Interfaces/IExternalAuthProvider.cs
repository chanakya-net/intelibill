using ErrorOr;
using Intelibill.Application.Common.Models;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Common.Interfaces;

public interface IExternalAuthProvider
{
    ExternalAuthProvider Provider { get; }
    Task<ErrorOr<ExternalUserInfo>> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
}
