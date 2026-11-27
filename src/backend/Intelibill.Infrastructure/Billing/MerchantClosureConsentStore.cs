using Intelibill.Shared.Web.Persistence;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Billing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantClosureConsentStore(ApplicationDbContext db, ShopAffiliationConsentStore authority,
    Intelibill.Infrastructure.Licensing.ShopLicenceFence fence, TimeProvider clock, IMembershipAuthorityRepository memberships)
    : IMerchantClosureValidationReceiver
{
    internal async Task<ErrorOr<ShopClosureReviewV1>> PreviewAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var access = await EnterAsync(caller, shop, ct);
            if (access.IsError)
                return access.Errors;
            var review = await ReadReviewAsync(access.Value, ct);
            await transaction.CommitAsync(ct);
            return review;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return MerchantBillingErrors.Unavailable;
        }
    }

    private async Task<ErrorOr<ClosureAccess>> EnterAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct)
    {
        if (!await authority.IsOwnerAsync(caller, shop, ct))
            return ShopAffiliationConsentErrors.OwnerRequired;

        var lease = await fence.EnterAsync(shop, LicenceAction.History, ct);
        if (lease.IsError)
            return lease.Errors;
        await db.SetLockTimeoutAsync(LockWait.Brief, ct);
        var owner = await authority.LockOwnerAsync(caller, shop, ct);
        if (owner is null)
            return ShopAffiliationConsentErrors.OwnerRequired;
        if (lease.Value.CurrentDecision() is { Enforced: true, Denial: { } denied })
            return denied;

        var binding = await db.Set<ShopLicenseState>().AsNoTracking().SingleOrDefaultAsync(row => row.ShopId == shop, ct);
        if (binding is null)
            return Error.Conflict("Closure.AffiliationUnavailable", "The shop affiliation needs review before closure.");
        return new ClosureAccess(binding, owner);
    }

    private sealed record ClosureAccess(ShopLicenseState Binding, ShopAffiliationConsentStore.LiveOwner Owner);
}
