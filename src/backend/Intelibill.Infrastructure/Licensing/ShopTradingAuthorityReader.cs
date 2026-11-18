using System.Data;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Billing;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;

namespace Intelibill.Infrastructure.Licensing;

internal sealed class ShopTradingAuthorityReader(ApplicationDbContext db, ILiveSessionAuthority authority, IShopLicenceFence fence)
    : IShopTradingAuthorityReader
{
    public async Task<ErrorOr<ShopTradingAuthorityV1>> ReadAsync(SessionClaims session, Guid shopId, CancellationToken ct)
    {
        if (shopId == Guid.Empty || session.ActiveShopId != shopId)
            return MerchantSubscriptionStatusErrors.ShopScope;

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            if (!await authority.IsValidAsync(session, ct))
                return MerchantBillingErrors.SessionInvalid;

            var result = await fence.EnterAsync(shopId, LicenceAction.NewTrading, ct);
            if (result.IsError)
                return result.Errors;

            if (!await authority.IsValidAsync(session, ct))
                return MerchantBillingErrors.SessionInvalid;

            var decision = result.Value.Decision;
            await transaction.CommitAsync(ct);
            return decision is { Outcome: LicenceOutcome.Allowed, AuthorityVerifiedAt: { } verified, ValidUntil: { } until }
                ? new ShopTradingAuthorityV1(1, shopId, verified, until, decision.Enforced)
                : decision.Denial ?? LicenceErrors.VerificationUnavailable;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return LicenceErrors.VerificationUnavailable;
        }
    }
}
