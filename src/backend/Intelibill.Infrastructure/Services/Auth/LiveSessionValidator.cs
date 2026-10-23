using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace Intelibill.Infrastructure.Services.Auth;

public sealed class LiveSessionValidator(ApplicationDbContext db)
{
    public async Task<bool> IsValidAsync(
        Guid userId,
        long version,
        Guid? activeShopId,
        string? activeShopRole,
        CancellationToken ct)
    {
        var accountIsLive = await db.Users.AsNoTracking().AnyAsync(
            u => u.Id == userId
                && u.SecurityVersion == version
                && u.IsLoginEnabled
                && (u.Email == null || u.IsEmailVerified || u.ExternalLogins.Any()),
            ct);

        if (!accountIsLive)
            return false;

        if (activeShopId is null)
            return true;

        if (!Enum.TryParse<ShopRole>(activeShopRole, ignoreCase: true, out var claimedRole))
            return false;

        // The token's shop scope is checked live rather than trusted from the claim. Suspending or
        // removing one membership therefore ends access to that shop at once while the same
        // account's sessions scoped to its other shops keep working, and an Owner or Manager role
        // recorded in an older token cannot authorize anything after the membership that granted
        // it changed.
        return await db.ShopMemberships.AsNoTracking().AnyAsync(
            sm => sm.UserId == userId
                && sm.ShopId == activeShopId.Value
                && sm.Status == MembershipStatus.Active
                && sm.Role == claimedRole,
            ct);
    }
}
