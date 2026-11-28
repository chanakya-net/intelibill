using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.OfflineReconciliation.Commands.SubmitRecoveryBundle;

/// <summary>Decides whether the caller's live session and shop role (owner or manager) may submit a recovery bundle.</summary>
public sealed class RecoveryOperatorAuthorizer(
    IUserRepository userRepository,
    ILiveSessionAuthority? liveSessionAuthority = null)
{
    internal async Task<Error?> AuthorizeOperatorAsync(Guid shopId, Guid actorUserId, SessionClaims? session, CancellationToken ct)
    {
        if (liveSessionAuthority is not null && await liveSessionAuthority.SessionDenialAsync(session, actorUserId, shopId, ct) is { } sessionDenied)
            return sessionDenied;

        var role = await ReconciliationActor.ResolveRoleAsync(userRepository, actorUserId, shopId, ct);
        return role.IsError || role.Value is not (ShopRole.Owner or ShopRole.Manager)
            ? Errors.Recovery.NotAuthorized
            : null;
    }
}
