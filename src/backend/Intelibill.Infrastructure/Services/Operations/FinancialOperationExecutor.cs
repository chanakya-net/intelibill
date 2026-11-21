using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Operations;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Services.Operations;

/// <summary>
/// Reserves, runs and completes a financial operation in one PostgreSQL transaction.
/// </summary>
/// <remarks>
/// <para>
/// The reservation is <c>INSERT … ON CONFLICT DO NOTHING</c>. When a concurrent transaction holds an
/// uncommitted row with the same scope, PostgreSQL blocks the insert until that transaction ends:
/// if it committed, the insert affects no row and this attempt replays the committed receipt; if it
/// rolled back, the insert succeeds and this attempt runs the operation. No unique violation is
/// ever raised, so no aborted transaction is ever continued.
/// </para>
/// <para>
/// After a reservation loses, the next Read Committed statement can see the winning receipt.
/// Supplier-return and receipt-reversal replays read it under the recovery fence. Other replays
/// keep their existing post-rollback read.
/// </para>
/// </remarks>
internal sealed class FinancialOperationExecutor(ApplicationDbContext context, IShopLicenceFence fence) : IFinancialOperationExecutor
{
    private const int ResponseVersion = 1;

    private static readonly JsonSerializerOptions ResponseSerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ErrorOr<T>> ExecuteAsync<T>(
        FinancialOperationScope scope,
        Guid actorUserId,
        Guid targetId,
        string requestHash,
        Func<CancellationToken, Task<ErrorOr<T>>> action,
        CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A financial operation must own its transaction; it cannot join an open one.");

        var receiptId = Guid.NewGuid();
        var reservedAt = DateTimeOffset.UtcNow;
        Error? replayDenied;

        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            var reserved = await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO operation_receipts
                    (id, shop_id, operation_type, operation_key, request_hash, hash_version,
                     actor_user_id, target_id, status, response_version, created_at)
                VALUES
                    ({receiptId}, {scope.ShopId}, {(int)scope.OperationType}, {scope.Key}, {requestHash},
                     {FinancialOperationHasher.Version}, {actorUserId}, {targetId},
                     {(int)OperationReceiptStatus.Pending}, {ResponseVersion}, {reservedAt})
                ON CONFLICT (shop_id, operation_type, operation_key) DO NOTHING
                """,
                cancellationToken);

            if (reserved == 1)
            {
                ErrorOr<T> result;
                try
                {
                    var denied = await SuspensionDenialAsync(scope, replay: false, cancellationToken);
                    result = denied is { } refusal ? refusal : await action(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    context.ChangeTracker.Clear();
                    throw;
                }

                if (result.IsError)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    context.ChangeTracker.Clear();
                    return result;
                }

                var responseBody = JsonSerializer.Serialize(result.Value, ResponseSerializerOptions);
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE operation_receipts
                    SET status = {(int)OperationReceiptStatus.Completed},
                        response_body = {responseBody}::jsonb,
                        completed_at = {DateTimeOffset.UtcNow}
                    WHERE id = {receiptId}
                    """,
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return result;
            }

            if (scope.OperationType is FinancialOperationType.SupplierReturn or FinancialOperationType.SupplierReturnCredit
                or FinancialOperationType.PurchaseReceiptReversal or FinancialOperationType.StockCount
                or FinancialOperationType.ReceiptCostCorrection or FinancialOperationType.BatchDisposition)
            {
                var outcome = await ReplayUnderFenceAsync<T>(scope, requestHash, cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                context.ChangeTracker.Clear();
                return outcome;
            }

            replayDenied = await SuspensionDenialAsync(scope, replay: true, cancellationToken);
            await transaction.RollbackAsync(cancellationToken);
        }

        context.ChangeTracker.Clear();
        return replayDenied is { } replayRefusal ? replayRefusal : await ReplayAsync<T>(scope, requestHash, cancellationToken);
    }

    private Task<Error?> SuspensionDenialAsync(FinancialOperationScope scope, bool replay, CancellationToken ct) =>
        scope.SuspensionAction is { } action
            ? fence.ReadDenialAsync(scope.ShopId, replay ? LicenceAction.Recovery : action, ct)
            : Task.FromResult<Error?>(null);

    private async Task<ErrorOr<T>> ReplayUnderFenceAsync<T>(
        FinancialOperationScope scope,
        string requestHash,
        CancellationToken ct)
    {
        var lease = await fence.EnterAsync(scope.ShopId, LicenceAction.Recovery, ct);
        if (lease.IsError) return lease.Errors;
        if (lease.Value.CurrentDecision().EnforcedDenial() is { } admissionDenial) return admissionDenial;

        if (scope.RecheckReplayAuthority is { } authorize && await authorize(ct) is { } authorityDenied)
            return authorityDenied;

        var outcome = await ReplayAsync<T>(scope, requestHash, ct);
        if (scope.RecheckReplayAuthority is { } reauthorize && await reauthorize(ct) is { } readAuthorityDenied)
            return readAuthorityDenied;
        if (lease.Value.CurrentDecision().EnforcedDenial() is { } readDenial) return readDenial;
        return outcome;
    }

    private async Task<ErrorOr<T>> ReplayAsync<T>(
        FinancialOperationScope scope,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var receipt = await context.OperationReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.ShopId == scope.ShopId
                    && r.OperationType == scope.OperationType
                    && r.OperationKey == scope.Key
                    && r.Status == OperationReceiptStatus.Completed,
                cancellationToken)
            ?? throw new InvalidOperationException("A conflicting operation receipt was not visible after the reservation.");

        if (!string.Equals(receipt.RequestHash, requestHash, StringComparison.Ordinal))
            return Errors.Operation.PayloadMismatch;

        return JsonSerializer.Deserialize<T>(receipt.ResponseBody!, ResponseSerializerOptions)!;
    }
}
