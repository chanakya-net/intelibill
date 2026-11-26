using System.Text.Json;
using ErrorOr;
using Intelibill.Domain.Entities;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

public sealed partial class EntitlementProjectionStore
{
    private async Task<ErrorOr<Success>> EnterSnapshotAsync(EntitlementSnapshotV1 s, CancellationToken ct)
    {
        if (s.EventId != s.AffiliationRequestId) return await EnterAsync(s.AccountId, s.ShopId, ct);
        await db.Database.ExecuteSqlAsync($"""
            SELECT set_config('lock_timeout',{ShopLicenceFence.LockTimeout},true),set_config('app.active_shop_id',{s.ShopId.ToString("D")},true),
                set_config('app.entitlement_account_id',{s.AccountId.ToString("D")},true)
            """, ct);
        var source = await db.Database.SqlQuery<Guid?>($"""
            SELECT source_account_id AS "Value" FROM public.merchant_transfer_decisions WHERE shop_id={s.ShopId}
                AND transfer_id={s.AffiliationRequestId} AND destination_account_id={s.AccountId} AND outcome='Consumed'
            """).SingleOrDefaultAsync(ct);
        if (source is null) return await EnterAsync(s.AccountId, s.ShopId, ct);
        foreach (var id in new[] { source.Value, s.AccountId }.Order())
            await db.Database.ExecuteSqlAsync($"SELECT public.take_merchant_licence_fence({id},NULL,false)", ct);
        await db.Database.ExecuteSqlAsync($"SELECT public.take_merchant_licence_fence(NULL,{s.ShopId},false)", ct);
        return Result.Success;
    }

    private async Task<bool> MayActivateTransferAsync(EntitlementSnapshotV1 s, ShopLicenseState predecessor,
        CancellationToken ct)
    {
        if (s.EventId != s.AffiliationRequestId || s.Shop is not { } next || next.Revision <= predecessor.Revision)
            return false;
        var bytes = await db.Database.SqlQuery<byte[]>($"""
            SELECT command AS "Value" FROM public.merchant_transfer_decisions WHERE transfer_id={s.EventId}
                AND shop_id={s.ShopId} AND destination_account_id={s.AccountId} AND source_account_id={predecessor.AccountId}
                AND affiliation_id={predecessor.AffiliationRequestId} AND outcome='Consumed'
                AND NOT EXISTS (SELECT FROM public.merchant_transfer_decisions a WHERE a.transfer_id={s.EventId} AND a.outcome='Aborted')
            """).SingleOrDefaultAsync(ct);
        if (bytes is null) return false;
        var c = JsonSerializer.Deserialize<TransferCommandV1>(bytes, EntitlementSnapshotsV1.Json)!;
        var now = EntitlementSnapshotsV1.Instant(businessClock ?? clock);
        if (now < c.CutoverAt || next.StartsAt < c.CutoverAt || next.AccessEndsAt > c.PaidThroughAt ||
            next.MemberAllowance > c.MemberAllowance) return false;
        // Serialize activation with a change to the exact owner epoch, including disabling their sign-in.
        await db.Database.ExecuteSqlAsync($"SELECT id FROM public.users WHERE id={c.ShopOwner} FOR UPDATE", ct);
        await db.Database.ExecuteSqlAsync($"SELECT id FROM public.shops WHERE id={c.ShopId} FOR UPDATE", ct);
        await db.Database.ExecuteSqlAsync($"SELECT id FROM public.shop_memberships WHERE id={c.MembershipId} FOR UPDATE", ct);
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.entitlement_account_id',{c.SourceAccountId.ToString("D")},true)", ct);
        var ready = await db.Database.SqlQuery<bool>($"""
            SELECT reason IS NULL AND permit_count=0 AND (SELECT count(*) FROM public.shop_memberships
                WHERE shop_id={c.ShopId} AND status='Active')<={c.MemberAllowance} AS "Value"
            FROM public.coverage_transfer_shop_inspect({c.SourceAccountId},{c.ShopId},{c.AffiliationId},{c.TransferId},{c.ShopConsent},
                {c.ShopOwner},{c.MembershipId},{c.AuthorityRevision},{c.ProposalRevision},{c.TermsDigest},{now})
            """).SingleOrDefaultAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.entitlement_account_id',{s.AccountId.ToString("D")},true)", ct);
        return ready;
    }
}
