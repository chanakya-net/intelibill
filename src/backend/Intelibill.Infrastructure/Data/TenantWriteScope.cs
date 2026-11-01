using Intelibill.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Data;

/// <inheritdoc cref="ITenantWriteScope"/>
internal sealed class TenantWriteScope(ApplicationDbContext context) : ITenantWriteScope
{
    public async Task EnterAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "A tenant write scope requires an open transaction. set_config is transaction-local, " +
                "so without one the elevated shop would be written to the session and carried by the " +
                "pooled connection into the next request.");
        }

        // The third argument is is_local: the setting reverts when the transaction
        // ends, whether it commits or rolls back.
        await context.Database.ExecuteSqlAsync(
            $"select set_config('app.active_shop_id', {shopId.ToString()}, true)",
            cancellationToken);
    }
}
