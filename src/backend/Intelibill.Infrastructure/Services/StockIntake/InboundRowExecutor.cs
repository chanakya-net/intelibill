using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Intelibill.Infrastructure.Services.StockIntake;

/// <summary>
/// Runs each intake row in its own DI scope, so a row's tracked entities, caches and any
/// aborted transaction die with that scope and never reach the next row.
/// </summary>
/// <remarks>
/// <para>
/// An unkeyed (legacy) row commits with its single <c>SaveChanges</c>; any exception there
/// propagates as it always did, because a failed commit cannot prove that nothing was saved.
/// </para>
/// <para>
/// The scope resolves the request's session context through <c>IHttpContextAccessor</c> like any
/// other scope in the request, so every row's connection carries the same RLS identity.
/// </para>
/// </remarks>
internal sealed partial class InboundRowExecutor(
    IServiceScopeFactory scopeFactory,
    ILogger<InboundRowExecutor> logger) : IInboundRowExecutor
{
    public async Task<ErrorOr<AddInventoryResultDto>> ExecuteAsync(
        Guid shopId,
        Guid actorUserId,
        InboundRowOperation? operation,
        InboundInventoryLineInput row,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var processor = services.GetRequiredService<IInboundInventoryLineProcessor>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        if (operation is null)
            return await PostingTransaction.RunAsync(unitOfWork, ReceiveAsync, cancellationToken);

        // Only a failure inside the action is reported as retryable: the executor has not yet
        // committed and rolls the row back. A failure while reserving, completing or committing the
        // receipt, or while replaying it, may follow a commit, so it propagates (500) and the client
        // resends the same keys instead of trusting a "nothing saved" it cannot rely on.
        var executor = services.GetRequiredService<IFinancialOperationExecutor>();
        return await executor.ExecuteAsync(
            new FinancialOperationScope(shopId, FinancialOperationType.BulkIntakeRow, operation.Key),
            actorUserId,
            shopId,
            operation.RequestHash,
            ReceiveRetryablyAsync,
            cancellationToken);

        async Task<ErrorOr<AddInventoryResultDto>> ReceiveRetryablyAsync(CancellationToken ct)
        {
            try
            {
                return await ReceiveAsync(ct);
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                // The row ran inside the executor's open transaction, which never commits after an
                // error result. The scope and its context are discarded with this row.
                LogRowNotSaved(logger, shopId, exception.GetType().Name);
                return Errors.Inventory.RowRetryable;
            }
        }

        async Task<ErrorOr<AddInventoryResultDto>> ReceiveAsync(CancellationToken ct)
        {
            // The row's own transaction (the executor's, or the plain one for an unkeyed row) carries
            // the fence, so each row is authorized against its own business date, not once per manifest.
            var shop = await services.GetRequiredService<IShopRepository>().GetByIdAsync(shopId, ct);
            if (shop is null)
                return Errors.Shop.ShopNotFound;

            var clock = services.GetRequiredService<IBusinessClock>();
            var postedAt = (row.PerformedAt ?? clock.UtcNow).ToUniversalTime();
            var fence = await services.GetRequiredService<IPostingPeriodPolicy>().AuthorizeForWriteAsync(
                shopId, clock.GetBusinessDate(postedAt, shop.BusinessTimeZoneId), ct);
            if (fence.IsError)
                return fence.Errors;

            var result = await processor.ProcessAsync(
                shopId,
                row,
                actorUserId,
                new ItemResolutionContext(),
                new InventoryUpdateContext(),
                ct);

            if (result.IsError)
                return result.Errors;

            await unitOfWork.SaveChangesAsync(ct);
            return result.Value.ToResultDto();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Bulk intake row in shop {ShopId} was not saved and remains retryable ({ExceptionType}).")]
    private static partial void LogRowNotSaved(ILogger logger, Guid shopId, string exceptionType);
}
