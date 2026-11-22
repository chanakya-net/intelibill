using Intelibill.Application.Common.Security;

namespace Intelibill.Application.Features.Users.Commands.AcceptShopInvitation;

/// <summary>
/// Redeems an invitation from the invited person's own authenticated account.
/// </summary>
public sealed record AcceptShopInvitationCommand(
    Guid ActorUserId,
    Guid InvitationId,
    string Token)
{
    /// <summary>The calling device session, revalidated under the actor's account lock.</summary>
    public SessionClaims? Session { get; init; }
}
