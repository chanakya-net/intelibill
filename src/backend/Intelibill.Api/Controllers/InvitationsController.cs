using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Users.Commands.AcceptShopInvitation;
using Intelibill.Application.Features.Users.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

/// <summary>
/// Acceptance lives outside <c>/api/shops</c> on purpose: the invited person acts as themselves,
/// before they have any membership in — or any session scoped to — the inviting shop.
/// </summary>
[ApiController]
[Route("api/invitations")]
[Authorize]
public sealed class InvitationsController : AuthenticatedControllerBase
{
    public InvitationsController(IMessageBus bus) : base(bus)
    {
    }

    [HttpPost("{invitationId:guid}/accept")]
    public async Task<IActionResult> AcceptInvitation(
        Guid invitationId,
        [FromBody] AcceptInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<MembershipDto>>(
            new AcceptShopInvitationCommand(UserId!.Value, invitationId, request.Token),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}

public sealed record AcceptInvitationRequest(string Token);
