using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.PurchaseOrders.Services;

public sealed class PurchaseOrderHistory(
    IUnitOfWork unitOfWork, IShopRepository shops, ILiveSessionAuthority sessions, IShopLicenceFence fence)
{
    public Task<ErrorOr<T>> ReadAsync<T>(Guid actor, Guid shop, SessionClaims? session,
        Func<CancellationToken, Task<ErrorOr<T>>> read, CancellationToken ct) =>
        PostingTransaction.RunAsync<T>(unitOfWork, async token =>
        {
            if (await CheckAuthorityAsync(actor, shop, session, token) is { } denied) return denied;
            var lease = await fence.EnterAsync(shop, LicenceAction.History, token);
            if (lease.IsError) return lease.Errors;
            if (lease.Value.CurrentDecision().EnforcedDenial() is { } admissionDenial) return admissionDenial;

            var result = await read(token);
            if (await CheckAuthorityAsync(actor, shop, session, token) is { } afterRead) return afterRead;
            if (lease.Value.CurrentDecision().EnforcedDenial() is { } readDenial) return readDenial;
            return result;
        }, ct);

    private async Task<Error?> CheckAuthorityAsync(Guid actor, Guid shop, SessionClaims? session, CancellationToken ct)
    {
        if (session is null || session.UserId != actor || session.ActiveShopId != shop ||
            !await sessions.IsValidAsync(session, ct))
            return Errors.Auth.SessionInvalidated;
        return await shops.GetActiveRoleAsync(actor, shop, ct) is null ? Errors.Shop.MembershipNotFound : null;
    }
}
