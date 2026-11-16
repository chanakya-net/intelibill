using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Licensing.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

/// <summary>
/// Licensing-owned membership check: the actor's active role in the account, locked FOR SHARE in the
/// delegation transaction so a concurrent removal waits until the use commits or rolls back.
/// </summary>
internal sealed class BillingAccountMembershipAuthority(LicensingDbContext db) : IBillingAccountDelegationAuthority
{
    public async Task<ErrorOr<Success>> AuthorizeAsync(Guid accountId, Guid actorId, string action,
        CancellationToken cancellationToken)
    {
        var roles = await db.Database.SqlQuery<int>($"""
            SELECT role AS "Value"
            FROM public.billing_account_members
            WHERE account_id = {accountId} AND merchant_user_id = {actorId}
              AND status = {(int)BillingAccountMemberStatus.Active}
            FOR SHARE
            """).ToListAsync(cancellationToken);
        return roles is [var role] && BillingAccountRules.Allows((BillingAccountRole)role, action)
            ? Result.Success
            : BillingDelegationErrors.Denied;
    }
}
