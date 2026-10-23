namespace Intelibill.Application.Features.Users.DTOs;

/// <summary>
/// One member as a shop administrator sees them. <c>Status</c> and <c>MembershipVersion</c> are
/// shop-scoped; the global login-enabled flag is deliberately absent, because shop administration
/// no longer reaches the account behind the membership.
/// </summary>
public sealed record ShopUserDto(
    Guid UserId,
    string FirstName,
    string LastName,
    string? Email,
    string? PhoneNumber,
    string Role,
    string Status,
    long MembershipVersion,
    IReadOnlyList<Guid> ShopIds,
    string? Language = null);
