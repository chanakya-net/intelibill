using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Users.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

/// <summary>
/// The caller's own signed-in devices. Everything here is scoped to the account the request
/// authenticated as — there is no route to another account's sessions, and a session id belonging
/// to someone else is simply not found.
/// </summary>
[ApiController]
[Route("api/users/me/sessions")]
[Authorize]
public sealed class SessionsController(IMessageBus bus, IAuthSessionService sessions)
    : AuthenticatedControllerBase(bus)
{
    [HttpGet]
    public async Task<IActionResult> GetMySessions(CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;
        var session = CheckSession();
        if (session is not null) return session;

        var result = await sessions.ListAsync(UserId!.Value, SessionId!.Value, cancellationToken);

        return result.ToActionResult(Ok);
    }

    /// <summary>
    /// Ends one device. Revoking the device making the request is allowed and signs it out, which is
    /// what someone sitting at a shared terminal wants.
    /// </summary>
    [HttpDelete("{sessionId:guid}")]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await sessions.RevokeAsync(UserId!.Value, sessionId, cancellationToken);

        return result.ToActionResult(_ => NoContent());
    }

    /// <summary>
    /// Ends every device, the caller's own included. Someone reaching for this has lost track of
    /// where they are signed in, so the one device it could spare is the one they cannot check.
    /// </summary>
    [HttpPost("revoke-all")]
    public async Task<IActionResult> RevokeAllSessions(CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await sessions.RevokeAllAsync(UserId!.Value, cancellationToken);

        return result.ToActionResult(_ => NoContent());
    }
}
