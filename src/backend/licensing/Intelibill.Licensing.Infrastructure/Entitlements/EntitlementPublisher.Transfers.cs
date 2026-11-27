using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

internal sealed partial class EntitlementPublisher
{
    internal async Task PublishTransferAsync(TransferCommandV1 c, CancellationToken ct)
    {
        // Initialize or refresh the destination account restriction before moving the one existing shop head.
        await ReadAccountAsync(c.DestinationAccountId, true, Now, ct);
        var moved = await db.Database.ExecuteSqlAsync($"""
            UPDATE public.entitlement_shop_heads SET account_id={c.DestinationAccountId}, revision=revision+1
            WHERE shop_id={c.ShopId} AND account_id={c.SourceAccountId}
            """, ct);
        if (moved != 1) throw new InvalidOperationException("Transfer predecessor entitlement is missing.");
        if (await AppendSnapshotAsync(c.DestinationAccountId, c.ShopId, false, Now, ct, c.TransferId) is null)
            throw new InvalidOperationException("Transfer successor entitlement is missing.");
    }
}
