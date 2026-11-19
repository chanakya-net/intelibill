using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Security;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Shops.Setup;

public sealed class ShopSetupAccess(
    IUnitOfWork unitOfWork,
    IShopRepository shops,
    IMembershipAuthorityRepository authority,
    ILiveSessionAuthority sessions,
    ITenantWriteScope tenantScope,
    IShopLicenceFence fence)
{
    private static readonly IReadOnlyList<string> RecoveryFields = Array.AsReadOnly(new[] { "name", "contactPerson", "mobileNumber" });
    private static readonly IReadOnlyList<string> ProfileFields = Array.AsReadOnly(
        new[] { "name", "contactPerson", "mobileNumber", "address", "city", "state", "pincode", "gstNumber" });

    internal async Task<ErrorOr<T>> WriteAsync<T>(SessionClaims? session, Guid actor, Guid shopId, LicenceAction action,
        Func<Shop, ShopLicenceLease, CancellationToken, Task<ErrorOr<T>>> write, CancellationToken ct)
    {
        if (!await IsLiveAsync(session, actor, ct)) return Errors.Auth.SessionInvalidated;
        await unitOfWork.BeginTransactionAsync(ct);
        var committed = false;
        try
        {
            var lease = await EnterOwnerAsync(actor, shopId, action, ct);
            if (lease.IsError) return lease.Errors;
            if (await LockActorAsync(actor, forShare: false, ct) is { } busy) return busy;
            var shop = await LockOwnerShopAsync(session!, shopId, ct);
            if (shop.IsError) return shop.Errors;

            if (Denial(lease.Value.CurrentDecision(), allowSetup: action == LicenceAction.BasicShopSetup,
                    allowRecovery: action == LicenceAction.BasicShopSetup) is { } denial)
                return denial;
            var result = await write(shop.Value, lease.Value, ct);
            if (result.IsError) return result;
            await unitOfWork.SaveChangesAsync(ct);
            await unitOfWork.CommitTransactionAsync(ct);
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

    internal Task<bool> IsLiveAsync(SessionClaims? session, Guid actor, CancellationToken ct) =>
        session is not null && session.UserId == actor ? sessions.IsValidAsync(session, ct) : Task.FromResult(false);

    internal async Task<ErrorOr<ShopLicenceLease>> EnterOwnerAsync(Guid actor, Guid shopId, LicenceAction action, CancellationToken ct)
    {
        var role = await shops.GetActiveRoleAsync(actor, shopId, ct);
        if (role is null) return Errors.Shop.MembershipNotFound;
        if (role != ShopRole.Owner) return Errors.Shop.UserIsNotOwner;
        return await EnterAsync(shopId, action, ct);
    }

    internal async Task<ErrorOr<ShopLicenceLease>> EnterAsync(Guid shopId, LicenceAction action, CancellationToken ct)
    {
        await tenantScope.EnterAsync(shopId, ct);
        return await fence.EnterAsync(shopId, action, ct);
    }

    /// <summary>
    /// Takes the actor's authority row while the licence fence is held. The wait is bounded so that a
    /// held account cannot keep the shared fence, and so the projection writer behind it, waiting.
    /// </summary>
    internal async Task<Error?> LockActorAsync(Guid actor, bool forShare, CancellationToken ct) =>
        await authority.TryLockUserAsync(actor, forShare, ct) == RowLockOutcome.TimedOut ? Errors.Licence.Busy : null;

    internal async Task<ErrorOr<Shop>> LockOwnerShopAsync(SessionClaims session, Guid shopId, CancellationToken ct)
    {
        var shopLock = await authority.TryLockShopAsync(shopId, ct);
        if (shopLock == RowLockOutcome.TimedOut) return Errors.Licence.Busy;
        if (shopLock == RowLockOutcome.NotFound) return Errors.Shop.ShopNotFound;
        if (await authority.TryLockMembershipAsync(shopId, session.UserId, ct) == RowLockOutcome.TimedOut)
            return Errors.Licence.Busy;
        var role = await shops.GetActiveRoleAsync(session.UserId, shopId, ct);
        if (role is null) return Errors.Shop.MembershipNotFound;
        if (role != ShopRole.Owner) return Errors.Shop.UserIsNotOwner;
        if (!await sessions.IsValidAsync(session, ct)) return Errors.Auth.SessionInvalidated;
        var shop = await shops.GetCurrentForSetupAsync(shopId, ct);
        return shop is null ? Errors.Shop.ShopNotFound : shop;
    }

    internal static Error? Denial(LicenceDecision decision, bool allowSetup = false, bool allowRecovery = false) =>
        !decision.Enforced || decision.Outcome == LicenceOutcome.Allowed ||
        (allowSetup && decision.Outcome == LicenceOutcome.RequiresOwnerSetup) ||
        (allowRecovery && decision.Outcome == LicenceOutcome.RequiresRecoveryNecessity)
            ? null
            : decision.Denial ?? decision.OperatingRefusal ?? Errors.Licence.VerificationUnavailable;

    internal static ShopSetupAccessDto Hint(Guid shopId, ShopRole role, LicenceDecision decision)
    {
        var owner = role == ShopRole.Owner;
        var prepare = owner && Denial(decision, allowSetup: true) is null;
        var recover = owner && Denial(decision, allowSetup: true, allowRecovery: true) is null;
        var operate = Denial(decision) is null;
        return new(shopId, 1, decision.Enforced, decision.Enforced ? decision.Status : "ObserveOnly",
            prepare ? ProfileFields : recover ? RecoveryFields : [], prepare, prepare, owner && operate, operate,
            operate && (!decision.Enforced || owner));
    }

    /// <summary>
    /// Runs a protected read inside the transaction that holds the shared licence fence, so a
    /// suspension either commits first and is refused here or waits until the read has finished.
    /// </summary>
    internal async Task<ErrorOr<T>> ReadAsync<T>(SessionClaims? session, Guid actor, Guid shopId,
        Func<ShopSetupAccessDto, CancellationToken, Task<ErrorOr<T>>> read, CancellationToken ct)
    {
        if (!await IsLiveAsync(session, actor, ct)) return Errors.Auth.SessionInvalidated;
        if (await shops.GetActiveRoleAsync(actor, shopId, ct) is null) return Errors.Shop.MembershipNotFound;
        await unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var lease = await EnterAsync(shopId, LicenceAction.BasicShopSetup, ct);
            if (lease.IsError) return lease.Errors;
            if (!await IsLiveAsync(session, actor, ct)) return Errors.Auth.SessionInvalidated;
            if (await shops.GetActiveRoleAsync(actor, shopId, ct) is not { } role) return Errors.Shop.MembershipNotFound;
            var decision = lease.Value.CurrentDecision();
            if (Denial(decision, allowSetup: true, allowRecovery: true) is { } denial) return denial;
            var result = await read(Hint(shopId, role, decision), ct);
            return Denial(lease.Value.CurrentDecision(), allowSetup: true, allowRecovery: true) is { } late ? late : result;
        }
        finally
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }
    }
}
