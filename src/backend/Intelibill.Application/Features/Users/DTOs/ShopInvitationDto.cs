namespace Intelibill.Application.Features.Users.DTOs;

/// <summary>
/// A pending or settled invitation as the inviting shop sees it. The single-use token is never
/// returned: it exists only in the message sent to the invited address.
/// </summary>
public sealed record ShopInvitationDto(
    Guid InvitationId,
    Guid ShopId,
    string Email,
    string Role,
    string Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);
