using ErrorOr;
using AuthErrors = Intelibill.Application.Common.Errors.Errors.Auth;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Security;
using Intelibill.Domain.Interfaces;

namespace Intelibill.Application.Common.Licensing;

public sealed class DefinitionCreationTransaction(
    IUnitOfWork unitOfWork, ILiveSessionAuthority authority, IShopLicenceFence fence)
{
    public async Task<ErrorOr<T>> RunAsync<T>(SessionClaims session,
        Func<ShopLicenceLease, CancellationToken, Task<ErrorOr<T>>> create, CancellationToken ct)
    {
        if (session.ActiveShopId is not { } shopId || !await authority.IsValidAsync(session, ct))
            return AuthErrors.SessionInvalidated;

        await unitOfWork.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            var lease = await fence.EnterAsync(shopId, LicenceAction.OperationalCapacity, ct);
            if (!await authority.IsValidAsync(session, ct))
                return AuthErrors.SessionInvalidated;
            if (lease.IsError)
                return lease.Errors;

            var result = await create(lease.Value, ct);
            if (result.IsError)
                return result;

            await unitOfWork.SaveChangesAndCommitAsync(ct);
            committed = true;
            return result;
        }
        finally
        {
            if (!committed)
            {
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                unitOfWork.ClearChanges();
            }
        }
    }
}
