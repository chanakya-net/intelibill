using System.Data.Common;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Application.Features.Sales.Services.Returns;

internal sealed class SaleReturnMutationCoordinator(
    ISaleRepository saleRepository,
    IShopRepository shopRepository,
    IPostingPeriodPolicy postingPeriodPolicy,
    IBusinessClock businessClock,
    IUnitOfWork unitOfWork)
    : ISaleReturnMutationCoordinator
{
    // PostgreSQL SQLSTATEs that end the attempt without any effect and are safe to retry.
    private const string LockNotAvailable = "55P03";
    private const string DeadlockDetected = "40P01";
    private const string SerializationFailure = "40001";

    public async Task<ErrorOr<TResult>> ExecuteAsync<TResult>(
        Guid shopId,
        Guid saleId,
        Func<CancellationToken, Task<ErrorOr<TResult>>> mutation,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        var committed = false;
        var failing = false;
        try
        {
            // The fence comes first: before the sale lock, and so before any customer, credit note
            // or batch lock the mutation takes after it.
            var shop = await shopRepository.GetByIdAsync(shopId, cancellationToken);
            if (shop is null)
                return Errors.Shop.ShopNotFound;

            var fence = await postingPeriodPolicy.AuthorizeForWriteAsync(
                shopId, businessClock.Today(shop.BusinessTimeZoneId), cancellationToken);
            if (fence.IsError)
                return fence.Errors;

            var outcome = await saleRepository.LockForReturnMutationAsync(shopId, saleId, cancellationToken);
            switch (outcome)
            {
                case RowLockOutcome.Acquired:
                    break;
                case RowLockOutcome.NotFound:
                    return Errors.Sale.NotFound(saleId);
                case RowLockOutcome.TimedOut:
                    return Errors.Sale.ReturnSaleBusy;
                default:
                    throw new InvalidOperationException($"Unknown sale lock outcome '{outcome}'.");
            }

            var result = await mutation(cancellationToken);
            if (result.IsError)
                return result;

            await unitOfWork.CommitTransactionAsync(cancellationToken);
            committed = true;
            return result;
        }
        catch (DbUpdateConcurrencyException)
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
            if (!committed)
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
