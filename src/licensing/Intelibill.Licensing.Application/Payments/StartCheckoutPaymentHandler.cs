using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed record CheckoutPaymentStart(CheckoutPaymentV1 Payment, bool Created);

/// <summary>
/// Commits the pinned attempt intention (delegation transaction, account lock), then asks the gateway for a
/// checkout session with no transaction open, then records the session in a short attempt-scoped transaction.
/// A replayed key never dispatches again.
/// </summary>
public sealed class StartCheckoutPaymentHandler(IBillingDelegationAccess access, ICheckoutOrderStore orders,
    IPaymentStore store, IPaymentGatewayResolver gateways, CheckoutPaymentStatusReader status, TimeProvider clock)
{
    public async Task<ErrorOr<CheckoutPaymentStart>> HandleAsync(VerifiedBillingDelegation proof, Guid account, Guid order,
        StartCheckoutPaymentV1 request, CancellationToken ct)
    {
        var begun = await access.ExecuteAsync<(StoredPaymentAttempt Attempt, bool Created)>(proof,
            (_, token) => BeginAsync(proof, account, order, request, token), ct);
        if (begun.IsError)
            return begun.Errors;
        var (attempt, created) = begun.Value;
        if (created)
            await DispatchAsync(attempt, ct);
        var read = await store.InAttemptScopeAsync(account, attempt.AttemptId, false,
            token => status.ReadAsync(account, order, token), ct);
        return read.IsError ? read.Errors : new CheckoutPaymentStart(read.Value, created);
    }

    private async Task<ErrorOr<(StoredPaymentAttempt, bool)>> BeginAsync(VerifiedBillingDelegation proof, Guid account,
        Guid order, StartCheckoutPaymentV1 request, CancellationToken ct)
    {
        if (!request.IsValid())
            return CheckoutPaymentErrors.Invalid;
        var hash = CheckoutPaymentsV1.StartHash(order, request);
        if (await store.FindAttemptAsync(account, request.OperationKey, true, ct) is { } prior)
            return prior.ActorId == proof.ActorId && prior.RequestHash == hash && prior.OrderId == order
                ? (prior, false) : BillingAccountErrors.PayloadMismatch;
        if (await orders.FindAsync(account, order, false, ct) is not { Quote: var quote })
            return CheckoutOrderErrors.Missing;
        var refusal = await RefuseAsync(account, quote, request, ct);
        if (refusal is { } error)
            return error;
        if (gateways.ForNewAttempts() is not { } gateway)
            return CheckoutPaymentErrors.Unavailable;

        var now = BillingAccountRules.StorageInstant(clock.GetUtcNow());
        if (now >= quote.ExpiresAt || !CheckoutRenewalPolicy.WindowOpen(quote, now))
            return CheckoutOrderErrors.Expired;
        var attempt = new NewPaymentAttempt(Guid.NewGuid(), account, order, request.OperationKey, hash, proof.ReplayId,
            gateway.Pin, quote.AmountPaise, quote.Currency, quote.SnapshotHash, now);
        await store.InsertAttemptAsync(attempt, ct);
        var stored = (await store.FindAttemptAsync(account, attempt.AttemptId, false, ct))!;
        await store.UseAttemptAsync(stored.AttemptId, ct);
        await store.AppendEventAsync(stored, PaymentEventKinds.AttemptStarted, proof.ActorId, now, ct);
        return (stored, true);
    }

    private async Task<Error?> RefuseAsync(Guid account, CheckoutQuoteV1 quote, StartCheckoutPaymentV1 request,
        CancellationToken ct)
    {
        if (quote.Acceptance is null)
            return CheckoutPaymentErrors.NotAccepted;
        if (quote.Revision != request.ExpectedRevision || quote.SnapshotHash != request.SnapshotHash)
            return CheckoutOrderErrors.Changed;
        if (await store.ManualVerifiedAtAsync(account, quote.OrderId, ct) is not null ||
            await store.FindOrderAttemptAsync(account, quote.OrderId, ct) is not null ||
            await store.HasOverlappingPaymentAsync(account, quote.OrderId, ct))
            return CheckoutPaymentErrors.AlreadyStarted;
        // Trial conversion and restored-access purchases (#872) are refused before any money moves.
        if (!CheckoutPaymentStatusReader.Startable(quote))
            return CheckoutPaymentErrors.Unsupported;
        var ids = await orders.ShopIdsAsync(account, quote.OrderId, ct);
        if ((await CheckoutRenewalPolicy.SourceAsync(orders, quote, ct))?.Hash() != quote.SourceHash)
            return CheckoutOrderErrors.Changed;
        return await store.AnyTrialAsync(account, ids, ct) ? CheckoutPaymentErrors.Unsupported : null;
    }

    private async Task DispatchAsync(StoredPaymentAttempt attempt, CancellationToken ct)
    {
        if (gateways.ForPin(attempt.Pin) is not { } gateway || gateway.Pin != attempt.Pin)
            return;
        var session = await PaymentGatewayCall.RunAsync(token => gateway.CreateCheckoutAsync(
            ReconcilePaymentHandler.Request(attempt), token), ct);
        if (session is not { Outcome: GatewayOutcome.Succeeded, Value: { } value } ||
            !ReconcilePaymentHandler.ValidSession(value))
            return;
        await store.InAttemptScopeAsync<Success>(attempt.AccountId, attempt.AttemptId, true, async token =>
        {
            var at = BillingAccountRules.StorageInstant(clock.GetUtcNow());
            if (await store.RecordSessionAsync(attempt, value, at, token))
                await store.AppendEventAsync(attempt, PaymentEventKinds.SessionRecorded, null, at, token);
            return Result.Success;
        }, ct);
    }
}
