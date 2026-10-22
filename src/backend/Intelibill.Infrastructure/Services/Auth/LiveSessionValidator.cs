using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
namespace Intelibill.Infrastructure.Services.Auth;

public sealed class LiveSessionValidator(ApplicationDbContext db)
{
    public Task<bool> IsValidAsync(Guid userId, long version, CancellationToken ct) =>
        db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.SecurityVersion == version && u.IsLoginEnabled && (u.Email == null || u.IsEmailVerified || u.ExternalLogins.Any()), ct);
}
