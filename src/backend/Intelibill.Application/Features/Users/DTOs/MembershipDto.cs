namespace Intelibill.Application.Features.Users.DTOs;

/// <summary>
/// One shop-scoped membership. Deliberately carries no global account fields (sign-in email,
/// phone, recovery state) and no other shop's id: a shop administrator sees the authority they
/// hold, not the identity behind it.
/// </summary>
public sealed record MembershipDto(
    Guid UserId,
    Guid ShopId,
    string DisplayName,
    string Role,
    string Status,
    long Version);
