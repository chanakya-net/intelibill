using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

/// <summary>
/// Raw statements without RETURNING: the account becomes visible only once its owner row exists, so
/// row-level security would refuse a returning insert of the account itself.
/// </summary>
internal sealed class BillingAccountStore(LicensingDbContext db) : IBillingAccountStore
{
    public async Task<bool> TryCreateAsync(BillingAccount account, BillingAccountMember owner,
        CancellationToken cancellationToken)
    {
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.billing_accounts
                (id, name, created_by_merchant_user_id, creation_key, creation_request_hash, created_at)
            VALUES ({account.Id}, {account.Name}, {account.CreatedByMerchantUserId}, {account.CreationKey},
                {account.CreationRequestHash}, {account.CreatedAt})
            ON CONFLICT DO NOTHING
            """, cancellationToken);
        if (inserted != 1)
            return false;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.billing_account_members
                (id, account_id, merchant_user_id, role, status, revision, created_at)
            VALUES ({owner.Id}, {owner.AccountId}, {owner.MerchantUserId}, {(int)owner.Role}, {(int)owner.Status},
                {owner.Revision}, {owner.CreatedAt})
            """, cancellationToken);
        return true;
    }

    public async Task<StoredBillingAccount?> FindAsync(Guid accountId, Guid actorId, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<AccountRow>($"""
            SELECT a.id, a.name, a.created_by_merchant_user_id, a.creation_request_hash, a.created_at, m.role
            FROM public.billing_accounts a
            JOIN public.billing_account_members m ON m.account_id = a.id
            WHERE a.id = {accountId} AND m.merchant_user_id = {actorId}
              AND m.status = {(int)BillingAccountMemberStatus.Active}
            """).ToListAsync(cancellationToken);
        return rows is [var row]
            ? new StoredBillingAccount(row.Id, row.Name, row.CreatedByMerchantUserId, row.CreationRequestHash,
                row.CreatedAt, (BillingAccountRole)row.Role)
            : null;
    }

    public async Task<IReadOnlyList<Guid>> ListActorAccountIdsAsync(Guid actorId, Guid? after, int limit,
        CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<Guid>($"""
            SELECT account_id AS "Value"
            FROM public.billing_account_members
            WHERE merchant_user_id = {actorId} AND status = {(int)BillingAccountMemberStatus.Active}
            AND account_id > {after ?? Guid.Empty}
            ORDER BY account_id
            LIMIT {limit}
            """).ToListAsync(cancellationToken);

    private sealed record AccountRow(Guid Id, string Name, Guid CreatedByMerchantUserId, byte[] CreationRequestHash,
        DateTimeOffset CreatedAt, int Role);
}
