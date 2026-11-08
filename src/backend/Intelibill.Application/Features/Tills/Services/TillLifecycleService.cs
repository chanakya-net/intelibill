using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Operations;
using Intelibill.Application.Common.Posting;
using Intelibill.Application.Common.Time;
using Intelibill.Application.Features.Tills.Commands.CreateTill;
using Intelibill.Application.Features.Tills.Commands.OpenTill;
using Intelibill.Application.Features.Tills.Commands.RecordTillCashMovement;
using Intelibill.Application.Features.Tills.Commands.CloseTill;
using Intelibill.Application.Features.Tills.Commands.ReviewTillClose;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using TillErrors = Intelibill.Application.Common.Errors.Errors.Till;

namespace Intelibill.Application.Features.Tills.Services;

public sealed class TillLifecycleService(ITillRepository tills, IShopRepository shops, IUserRepository users,
    IOperationReceiptRepository receipts, ITillSessionPolicy policy, ITillPostingService posting,
    IFinancialOperationExecutor executor, IPostingPeriodPolicy periods, IUnitOfWork work, IBusinessClock clock,
    TillDtoBuilder builder)
{
    public async Task<ErrorOr<TillDto>> CreateAsync(CreateTillCommand command, CancellationToken ct)
    {
        var validation = Validate(command, new CreateTillCommandValidator());
        if (validation.Count != 0) return validation;
        if (!IsManager(await shops.GetActiveRoleAsync(command.ActorUserId, command.ShopId, ct))) return TillErrors.Forbidden;
        var access = await ReceiptAccessAsync(command.ShopId, command.ActorUserId, FinancialOperationType.TillCreate, command.OperationKey, ct);
        if (access.IsError) return access.Errors;
        return await executor.ExecuteAsync<TillDto>(new(command.ShopId, FinancialOperationType.TillCreate, command.OperationKey.Trim()),
            command.ActorUserId, command.ShopId, FinancialOperationHasher.Compute(new { command.ActorUserId, Name = command.Name.Trim() }), async token =>
            {
                if (!policy.IsEnabled(command.ShopId)) return TillErrors.Disabled;
                var locked = await tills.LockCatalogAsync(command.ShopId, token);
                if (locked != RowLockOutcome.Acquired) return LockError(locked);
                if (!IsManager(await shops.GetActiveRoleAsync(command.ActorUserId, command.ShopId, token))) return TillErrors.Forbidden;
                if ((await tills.ListTillsAsync(command.ShopId, token)).Any(x => string.Equals(x.Name, command.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                    return Error.Conflict("Till.NameExists", "A till with this name already exists.");
                var till = Till.Create(command.ShopId, command.Name, command.ActorUserId, clock.UtcNow);
                if (till.IsError) return till.Errors;
                await tills.AddTillAsync(till.Value, token);
                await work.SaveChangesAsync(token);
                return new TillDto(till.Value.Id, till.Value.Name);
            }, ct);
    }

    public async Task<ErrorOr<TillSessionDto>> OpenAsync(OpenTillCommand command, CancellationToken ct)
    {
        var validation = Validate(command, new OpenTillCommandValidator());
        if (validation.Count != 0) return validation;
        var auth = await policy.AuthorizeAsync(command.ShopId, command.ActorUserId, command.CashierId, command.OverrideReason, ct);
        if (auth.IsError) return auth.Errors;
        var till = await tills.GetTillAsync(command.ShopId, command.TillId, ct);
        if (till is null) return TillErrors.NotFound;
        var access = await ReceiptAccessAsync(command.ShopId, command.ActorUserId, FinancialOperationType.TillOpen, command.OperationKey, ct);
        if (access.IsError) return access.Errors;
        return await executor.ExecuteAsync<TillSessionDto>(new(command.ShopId, FinancialOperationType.TillOpen, command.OperationKey.Trim()),
            command.ActorUserId, command.TillId, FinancialOperationHasher.Compute(new { command.ActorUserId, command.TillId, command.CashierId,
                OpeningFloat = FinancialOperationHasher.NormalizeAmount(command.OpeningFloat), OverrideReason = command.OverrideReason?.Trim() }), async token =>
            {
                if (!policy.IsEnabled(command.ShopId)) return TillErrors.Disabled;
                var shop = await shops.GetByIdAsync(command.ShopId, token);
                if (shop is null) return TillErrors.NotFound;
                var today = clock.Today(shop.BusinessTimeZoneId);
                var fence = await periods.AuthorizeForWriteAsync(command.ShopId, today, token);
                if (fence.IsError) return fence.Errors;
                var locked = await tills.LockTillAsync(command.ShopId, command.TillId, token);
                if (locked != RowLockOutcome.Acquired) return LockError(locked);
                auth = await policy.AuthorizeAsync(command.ShopId, command.ActorUserId, command.CashierId, command.OverrideReason, token);
                if (auth.IsError) return auth.Errors;
                if (await tills.GetOpenSessionAsync(command.ShopId, command.TillId, token) is not null)
                    return Error.Conflict("Till.AlreadyOpen", "This till already has an open session.");
                var names = await users.GetDisplayNamesAsync(command.ShopId, [command.CashierId], token);
                if (!names.TryGetValue(command.CashierId, out var cashierName)) return TillErrors.Forbidden;
                var opened = TillSession.Open(command.ShopId, till.Id, command.CashierId, till.Name, cashierName, command.ActorUserId,
                    clock.UtcNow, today, shop.BusinessTimeZoneId, command.OpeningFloat, command.OverrideReason);
                if (opened.IsError) return opened.Errors;
                await tills.AddSessionAsync(opened.Value, token);
                await work.SaveChangesAsync(token);
                return await builder.BuildAsync(opened.Value, token);
            }, ct);
    }

    public async Task<ErrorOr<TillSessionDto>> MoveAsync(RecordTillCashMovementCommand command, CancellationToken ct)
    {
        var validation = Validate(command, new RecordTillCashMovementCommandValidator());
        if (validation.Count != 0) return validation;
        var authorized = await AuthorizeSessionAsync(command.ShopId, command.ActorUserId, command.SessionId, command.OverrideReason, ct);
        if (authorized.IsError) return authorized.Errors;
        var access = await ReceiptAccessAsync(command.ShopId, command.ActorUserId, FinancialOperationType.TillCashMovement, command.OperationKey, ct);
        if (access.IsError) return access.Errors;
        return await executor.ExecuteAsync<TillSessionDto>(new(command.ShopId, FinancialOperationType.TillCashMovement, command.OperationKey.Trim()),
            command.ActorUserId, command.SessionId, FinancialOperationHasher.Compute(new { command.ActorUserId, command.SessionId, command.Direction,
                Amount = FinancialOperationHasher.NormalizeAmount(command.Amount), Reason = command.Reason.Trim(), command.ExpectedVersion,
                OverrideReason = command.OverrideReason?.Trim() }), async token =>
            {
                if (!policy.IsEnabled(command.ShopId)) return TillErrors.Disabled;
                var original = authorized.Value;
                var fence = await periods.AuthorizeForWriteAsync(command.ShopId, original.BusinessDate, token);
                if (fence.IsError) return fence.Errors;
                var acquired = await posting.AcquireAsync(command.ShopId, command.ActorUserId, new(command.SessionId, command.OverrideReason), original.BusinessDate, token);
                if (acquired.IsError) return acquired.Errors;
                var session = acquired.Value!;
                if (session.Version != command.ExpectedVersion) return TillErrors.VersionConflict;
                var appended = await posting.AppendAsync(session, new(TillMovementSource.ManualCash, Guid.NewGuid(),
                    command.Direction == 1m ? "cash-in" : "cash-out", [new(ReceiptTenderMethod.Cash, command.Amount, null, null, null, null)],
                    command.Direction, clock.UtcNow, command.OperationKey.Trim(), command.Reason.Trim()), command.ActorUserId, token);
                if (appended.IsError) return appended.Errors;
                await work.SaveChangesAsync(token);
                return await builder.BuildAsync(session, token);
            }, ct);
    }

    public async Task<ErrorOr<TillSessionDto>> CloseAsync(CloseTillCommand command, CancellationToken ct)
    {
        var validation = Validate(command, new CloseTillCommandValidator());
        if (validation.Count != 0) return validation;
        var authorized = await AuthorizeSessionAsync(command.ShopId, command.ActorUserId, command.SessionId, command.OverrideReason, ct);
        if (authorized.IsError) return authorized.Errors;
        var access = await ReceiptAccessAsync(command.ShopId, command.ActorUserId, FinancialOperationType.TillClose, command.OperationKey, ct);
        if (access.IsError) return access.Errors;
        return await executor.ExecuteAsync<TillSessionDto>(new(command.ShopId, FinancialOperationType.TillClose, command.OperationKey.Trim()),
            command.ActorUserId, command.SessionId, FinancialOperationHasher.Compute(new { command.ActorUserId, command.SessionId,
                CountedCash = FinancialOperationHasher.NormalizeAmount(command.CountedCash), Reason = command.Reason?.Trim(), command.ExpectedVersion,
                OverrideReason = command.OverrideReason?.Trim(), command.FinalizeOverdue }), async token =>
            {
                if (!policy.IsEnabled(command.ShopId)) return TillErrors.Disabled;
                var fence = await periods.AuthorizeForWriteAsync(command.ShopId, authorized.Value.BusinessDate, token);
                if (fence.IsError) return fence.Errors;
                var locked = await LockSessionAsync(command.ShopId, command.SessionId, token);
                if (locked.IsError) return locked.Errors;
                var session = locked.Value;
                var auth = await policy.AuthorizeAsync(command.ShopId, command.ActorUserId, session.CashierId, command.OverrideReason, token);
                if (auth.IsError) return auth.Errors;
                if (session.Version != command.ExpectedVersion) return TillErrors.VersionConflict;
                if (session.State != TillSessionState.Open) return TillErrors.Closed;
                var role = await shops.GetActiveRoleAsync(command.ActorUserId, command.ShopId, token);
                var today = clock.Today(session.BusinessTimeZoneId);
                if (today > session.BusinessDate && (!command.FinalizeOverdue || !IsManager(role))) return TillErrors.Overdue;
                var totals = await tills.GetTotalsAsync(command.ShopId, session.Id, token);
                var closed = session.Close(totals, command.CountedCash, command.ActorUserId, clock.UtcNow, today,
                    command.Reason, role == ShopRole.Staff, command.FinalizeOverdue, command.OverrideReason);
                if (closed.IsError) return closed.Errors;
                await tills.AddCloseAsync(closed.Value, token);
                await work.SaveChangesAsync(token);
                return await builder.BuildAsync(session, token);
            }, ct);
    }

    public async Task<ErrorOr<TillSessionDto>> ReviewAsync(ReviewTillCloseCommand command, CancellationToken ct)
    {
        var validation = Validate(command, new ReviewTillCloseCommandValidator());
        if (validation.Count != 0) return validation;
        if (!IsManager(await shops.GetActiveRoleAsync(command.ActorUserId, command.ShopId, ct))) return TillErrors.Forbidden;
        var session = await tills.GetSessionAsync(command.ShopId, command.SessionId, ct);
        if (session is null) return TillErrors.SessionNotFound;
        var access = await ReceiptAccessAsync(command.ShopId, command.ActorUserId, FinancialOperationType.TillCloseReview, command.OperationKey, ct);
        if (access.IsError) return access.Errors;
        return await executor.ExecuteAsync<TillSessionDto>(new(command.ShopId, FinancialOperationType.TillCloseReview, command.OperationKey.Trim()),
            command.ActorUserId, command.SessionId, FinancialOperationHasher.Compute(new { command.ActorUserId, command.SessionId,
                Reason = command.Reason.Trim(), command.ExpectedVersion }), async token =>
            {
                if (!policy.IsEnabled(command.ShopId)) return TillErrors.Disabled;
                var fence = await periods.AuthorizeForWriteAsync(command.ShopId, session.BusinessDate, token);
                if (fence.IsError) return fence.Errors;
                var locked = await LockSessionAsync(command.ShopId, command.SessionId, token);
                if (locked.IsError) return locked.Errors;
                session = locked.Value;
                if (!IsManager(await shops.GetActiveRoleAsync(command.ActorUserId, command.ShopId, token))) return TillErrors.Forbidden;
                if (session.Version != command.ExpectedVersion) return TillErrors.VersionConflict;
                var close = await tills.GetCloseAsync(command.ShopId, session.Id, token);
                if (close is null || !close.AwaitingReview) return Error.Conflict("Till.ReviewNotRequired", "There is no variance awaiting review.");
                if (await tills.GetReviewAsync(command.ShopId, close.Id, token) is not null)
                    return Error.Conflict("Till.AlreadyReviewed", "This immutable close already has a review.");
                var review = TillCloseReview.Record(close, command.ActorUserId, clock.UtcNow, command.Reason);
                if (review.IsError) return review.Errors;
                await tills.AddReviewAsync(review.Value, token);
                await work.SaveChangesAsync(token);
                return await builder.BuildAsync(session, token);
            }, ct);
    }

    private async Task<ErrorOr<TillSession>> AuthorizeSessionAsync(Guid shopId, Guid actorId, Guid sessionId, string? reason, CancellationToken ct)
    {
        if (await shops.GetActiveRoleAsync(actorId, shopId, ct) is null) return TillErrors.Forbidden;
        var session = await tills.GetSessionAsync(shopId, sessionId, ct);
        if (session is null) return TillErrors.SessionNotFound;
        var auth = await policy.AuthorizeAsync(shopId, actorId, session.CashierId, reason, ct);
        return auth.IsError ? auth.Errors : session;
    }
    private async Task<ErrorOr<TillSession>> LockSessionAsync(Guid shopId, Guid sessionId, CancellationToken ct)
    {
        var locked = await tills.LockSessionAsync(shopId, sessionId, ct);
        if (locked != RowLockOutcome.Acquired) return locked == RowLockOutcome.TimedOut ? TillErrors.Busy : TillErrors.SessionNotFound;
        var session = await tills.ReloadSessionAsync(shopId, sessionId, ct);
        return session is null ? TillErrors.SessionNotFound : session;
    }
    private async Task<ErrorOr<Success>> ReceiptAccessAsync(Guid shopId, Guid actorId, FinancialOperationType type, string key, CancellationToken ct)
    {
        var receipt = await receipts.GetCompletedAsync(shopId, type, key.Trim(), ct);
        if (receipt is not null && receipt.ActorUserId != actorId && !IsManager(await shops.GetActiveRoleAsync(actorId, shopId, ct)))
            return TillErrors.Forbidden;
        return Result.Success;
    }
    private static bool IsManager(ShopRole? role) => role is ShopRole.Owner or ShopRole.Manager;
    private static Error LockError(RowLockOutcome outcome) => outcome == RowLockOutcome.TimedOut ? TillErrors.Busy : TillErrors.NotFound;
    private static List<Error> Validate<T>(T command, AbstractValidator<T> validator) =>
        validator.Validate(command).Errors.Select(x => Error.Validation(x.PropertyName, x.ErrorMessage)).ToList();
}
