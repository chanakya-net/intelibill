using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class PasswordResetTokenRepository(ApplicationDbContext context)
    : RepositoryBase<PasswordResetToken>(context), IPasswordResetTokenRepository
{
    public async Task<PasswordResetToken?> GetValidByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(t => t.UserId == userId && !t.IsUsed && t.ExpiresAt > DateTimeOffset.UtcNow)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task InvalidateAllForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tokens = await DbSet
            .Where(t => t.UserId == userId && !t.IsUsed)
            .ToListAsync(cancellationToken);

        foreach (var t in tokens)
            t.MarkAsUsed();
    }
}
