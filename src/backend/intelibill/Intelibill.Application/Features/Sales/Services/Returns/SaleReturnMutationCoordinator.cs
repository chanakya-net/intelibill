using System.Data.Common;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Services.Returns;

internal sealed class SaleReturnMutationCoordinator(
    ISaleRepository saleRepository,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock,
    IUnitOfWork unitOfWork, Intelibill.Application.Features.Expiry.IExpiryPolicyFence expiryFence,
    IShopLicenceFence licenceFence)
    : ISaleReturnMutationCoordinator
{
    public DateTimeOffset? PostingInstant { get; private set; }

    // PostgreSQL SQLSTATEs that end the attempt without any effect and are safe to retry.
    private const string LockNotAvailable = "55P03";
    private const string DeadlockDetected = "40P01";
    private const string SerializationFailure = "40001";

    public Task<ErrorOr<TResult>> ExecuteAsync<TResult>(
        Guid shopId,
        Guid saleId,
        Func<CancellationToken, Task<ErrorOr<TResult>>> mutation,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(shopId, _ => Task.FromResult<ErrorOr<Guid>>(saleId),
            _ => Task.FromResult<ErrorOr<Success>>(Result.Success),
            (_, ct) => mutation(ct), true, cancellationToken);
    }

    public async Task<ErrorOr<TResult>> ExecuteAsync<TResult>(Guid shopId,
        Func<CancellationToken, Task<ErrorOr<Guid>>> resolveSale,
        Func<CancellationToken, Task<ErrorOr<Success>>> beforeBusinessLocks,
        Func<ShopLicenceLease, CancellationToken, Task<ErrorOr<TResult>>> mutation,
        bool ownsTransaction, CancellationToken cancellationToken)
    {
        if (ownsTransaction) await unitOfWork.BeginTransactionAsync(cancellationToken);
        var committed = false;
        var failing = false;
        try
        {
            var result = await ExecuteUnderFenceAsync(shopId, resolveSale, beforeBusinessLocks, mutation, cancellationToken);
            if (result.IsError) return result;
            if (ownsTransaction) await unitOfWork.CommitTransactionAsync(cancellationToken);
            committed = true;
            return result;
        }
        catch (Exception ex) when (unitOfWork.IsConcurrencyConflict(ex))
        {
            // A batch or aggregate the mutation changed was written by a sale or stock correction
            // that does not take the sale lock. Nothing was kept; the caller can reload and retry.
            return Errors.Sale.ReturnConcurrentChange;
        }
        catch (Exception ex) when (SqlStateOf(ex) == LockNotAvailable)
        {
            // A later lock (credit note, batch row at save) was held past the transaction's budget.
            return Errors.Sale.ReturnSaleBusy;
        }
        catch (Exception ex) when (SqlStateOf(ex) is DeadlockDetected or SerializationFailure)
        {
            return Errors.Sale.ReturnConcurrentChange;
        }
        catch (Exception) when (MarkFailing(ref failing))
        {
            throw; // Unreachable: the filter only records that an exception is on its way out.
        }
        finally
        {
            PostingInstant = null;
            if (ownsTransaction && !committed)
            {
                try
                {
                    // Not the request's token: an abandoned request must still give its locks back.
                    await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
                }
                catch (Exception) when (failing)
                {
                    // The exception already on its way out says what went wrong; a rollback that
                    // fails after it (broken connection) must not replace it.
                }

                unitOfWork.ClearChanges();
            }
        }
    }

    private async Task<ErrorOr<TResult>> ExecuteUnderFenceAsync<TResult>(Guid shopId,
        Func<CancellationToken, Task<ErrorOr<Guid>>> resolveSale,
        Func<CancellationToken, Task<ErrorOr<Success>>> beforeBusinessLocks,
        Func<ShopLicenceLease, CancellationToken, Task<ErrorOr<TResult>>> mutation,
        CancellationToken ct)
    {
        var licence = await licenceFence.EnterAsync(shopId, LicenceAction.SourceCorrection, ct);
        if (licence.IsError) return licence.Errors;
        if (SaleReturnSourcePolicy.Denial(licence.Value.CurrentDecision()) is { } denied) return denied;

        var sale = await resolveSale(ct);
        if (sale.IsError) return sale.Errors;

        var shop = await shopRepository.GetByIdAsync(shopId, ct);
        if (shop is null) return Errors.Shop.ShopNotFound;
        PostingInstant = businessClock.UtcNow;
        var period = await postingPeriodPolicy.AuthorizeForWriteAsync(
            shopId, businessClock.GetBusinessDate(PostingInstant.Value, shop.BusinessTimeZoneId), ct);
        if (period.IsError) return period.Errors;

        var expiry = await expiryFence.AcquireAsync(shopId, false, ct);
        if (expiry.IsError) return expiry.Errors;

        var ready = await beforeBusinessLocks(ct);
        if (ready.IsError) return ready.Errors;

        var outcome = await saleRepository.LockForReturnMutationAsync(shopId, sale.Value, ct);
        if (outcome != RowLockOutcome.Acquired)
            return outcome switch
            {
                RowLockOutcome.NotFound => Errors.Sale.NotFound(sale.Value),
                RowLockOutcome.TimedOut => Errors.Sale.ReturnSaleBusy,
                _ => throw new InvalidOperationException($"Unknown sale lock outcome '{outcome}'."),
            };

        if (SaleReturnSourcePolicy.Denial(licence.Value.CurrentDecision()) is { } lockDenial) return lockDenial;
        var result = await mutation(licence.Value, ct);
        return !result.IsError && SaleReturnSourcePolicy.Denial(licence.Value.CurrentDecision()) is { } writeDenial
            ? writeDenial : result;
    }

    private static bool MarkFailing(ref bool failing)
    {
        failing = true;
        return false;
    }

    private static string? SqlStateOf(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException { SqlState: { } sqlState })
                return sqlState;
        }

        return null;
    }
}
