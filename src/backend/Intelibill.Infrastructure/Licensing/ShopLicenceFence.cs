using System.Data;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Policies;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using LicenceErrors = Intelibill.Application.Common.Errors.Errors.Licence;

namespace Intelibill.Infrastructure.Licensing;

public sealed class ShopLicenceFence(ApplicationDbContext db, ILicenceActionPolicy policy, TimeProvider clock)
    : IShopLicenceFence
{
    internal const string LockTimeout = "3s";
    private const string Savepoint = "shop_licence_fence";

    public Task<ErrorOr<ShopLicenceLease>> EnterAsync(Guid shopId, LicenceAction action, CancellationToken cancellationToken)
    {
        // The second binding read must see a projection that committed while we waited for the lock.
        if (Transaction.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
            throw new InvalidOperationException("The shop licence fence requires a read-committed source transaction.");

        return WithinBudgetAsync(() => ReadUnderFenceAsync(shopId, action, cancellationToken), cancellationToken);
    }

    internal Task<ErrorOr<Success>> EnterProjectionAsync(Guid accountId, Guid shopId, CancellationToken ct) =>
        WithinBudgetAsync<Success>(async () =>
        {
            await LockAsync(accountId, shopId, shared: false, ct);
            return Result.Success;
        }, ct);

    private async Task<ErrorOr<ShopLicenceLease>> ReadUnderFenceAsync(Guid shopId, LicenceAction action, CancellationToken ct)
    {
        var scope = await db.Database.SqlQuery<Guid?>(
            $"SELECT nullif(current_setting('app.active_shop_id', true), '')::uuid AS \"Value\"").SingleAsync(ct);
        if (scope != shopId)
            return LicenceErrors.ScopeMismatch;

        // Discover the account before taking either lock. An initial projection can appear between
        // this read and the shop lock. Retry if the binding changed, so we never lock shop then account.
        var shops = db.Set<ShopLicenseState>().AsNoTracking().Where(row => row.ShopId == shopId);
        var accountId = await shops.Select(row => (Guid?)row.AccountId).SingleOrDefaultAsync(ct);
        await LockAsync(accountId, shopId, shared: true, ct);

        var shop = await shops.SingleOrDefaultAsync(ct);
        if (shop?.AccountId != accountId)
            return LicenceErrors.Busy;

        var account = accountId is { } id
            ? await db.Set<AccountLicenseRestriction>().AsNoTracking().SingleOrDefaultAsync(row => row.AccountId == id, ct)
            : null;
        var proofs = shop is null ? null : await ReadProofIssuanceAsync(shopId, ct);
        var now = clock.GetUtcNow();
        return new ShopLicenceLease(shopId, accountId, account?.Revision, shop?.Revision,
            policy.Decide(action, shop, account, proofs, now))
        {
            Reevaluate = () => policy.Decide(action, shop, account, proofs, clock.GetUtcNow()),
            EffectiveSuspension = LicenseProjectionPolicy.EffectiveSuspension(shop, account, now),
            MemberAllowance = shop?.MemberAllowance,
        };
    }

    // The account head may rest on a sibling shop's evidence, which row security rightly hides from this shop.
    internal async Task<LicenseProofIssuance?> ReadProofIssuanceAsync(Guid shopId, CancellationToken ct) =>
        (await db.Database.SqlQuery<LicenseProofIssuance>(
            $"SELECT * FROM public.merchant_licence_proof_issuance({shopId})").ToListAsync(ct)).SingleOrDefault();

    private async Task<ErrorOr<T>> WithinBudgetAsync<T>(Func<Task<ErrorOr<T>>> acquire, CancellationToken ct)
    {
        var transaction = Transaction;
        await transaction.CreateSavepointAsync(Savepoint, ct);
        ErrorOr<T> result;
        try
        {
            var previous = await db.Database.SqlQuery<string>(
                $"SELECT current_setting('lock_timeout') AS \"Value\"").SingleAsync(ct);
            await SetTimeoutAsync(LockTimeout, ct);
            result = await acquire();
            if (!result.IsError)
                await SetTimeoutAsync(previous, ct);
        }
        catch (Exception error) when (error.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable })
        {
            result = LicenceErrors.Busy;
        }
        catch
        {
            await UndoAsync(transaction);
            throw;
        }

        // PostgreSQL aborts a transaction on a lock timeout. Rollback also restores lock_timeout
        // and releases any partial locks; successful locks survive savepoint release until commit.
        if (result.IsError)
            await UndoAsync(transaction);
        else
            await transaction.ReleaseSavepointAsync(Savepoint, CancellationToken.None);
        return result;
    }

    private IDbContextTransaction Transaction => db.Database.CurrentTransaction
        ?? throw new InvalidOperationException("The shop licence fence requires an open transaction.");

    private Task<int> LockAsync(Guid? accountId, Guid shopId, bool shared, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT public.take_merchant_licence_fence({accountId}::uuid, {shopId}, {shared})", ct);

    private Task<int> SetTimeoutAsync(string timeout, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {timeout}, true)", ct);

    private static async Task UndoAsync(IDbContextTransaction transaction)
    {
        await transaction.RollbackToSavepointAsync(Savepoint, CancellationToken.None);
        await transaction.ReleaseSavepointAsync(Savepoint, CancellationToken.None);
    }
}
