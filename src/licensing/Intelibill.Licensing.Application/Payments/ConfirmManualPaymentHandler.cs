using ErrorOr;
using FluentValidation;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed class ConfirmManualPaymentHandler(IManualPaymentStore store, IPaymentStore payments, ICheckoutOrderStore orders,
    IManualPaymentEvidence evidence, IValidator<ConfirmManualPayment> validator, FulfilPaidOrderHandler fulfilment,
    CheckoutPaymentStatusReader status, IPaymentReviewStore reviews, IPaymentConfirmationStore confirmations,
    Changes.ApplyCapacityIncreaseHandler increases, Changes.IScheduledCapacityReductionStore reductions, TimeProvider clock)
{
    public Task<ErrorOr<ManualPaymentView>> ReadAsync(AdminSessionCaller caller, Guid account, Guid order,
        Guid? after, CancellationToken ct) => store.InManualScopeAsync<ManualPaymentView>(caller, account, order,
        false, false, async (_, token) =>
        {
            if (await AcceptedOrderAsync(account, order, token) is not { } quote) return ManualPaymentErrors.Missing;
            var payment = await status.ReadAsync(account, order, token);
            if (payment.IsError) return payment.Errors;
            return new ManualPaymentView(await OrderViewAsync(quote, after, token), evidence.Account, payment.Value,
                await store.OnlineFundsNeedReviewAsync(account, order, token), await store.ListManualAsync(account, order, token));
        }, ct);

    public Task<ErrorOr<ManualPaymentPreview>> PreviewAsync(AdminSessionCaller caller, Guid account, Guid order,
        ConfirmManualPayment? command, CancellationToken ct) => store.InManualScopeAsync<ManualPaymentPreview>(
        caller, account, order, false, true, async (_, token) =>
        {
            var input = await ValidateAsync(account, order, command, token);
            if (input.IsError) return input.Errors;
            var (quote, receiving, _) = input.Value;
            var decision = await fulfilment.DecideAsync(quote, BillingAccountRules.StorageInstant(command!.ReceivedAt), token);
            return new ManualPaymentPreview(await OrderViewAsync(quote, null, token), receiving,
                command!.ReceivedAt, decision.Reduction is not null ? "ReductionPending"
                    : decision.Increase is not null ? "CapacityPending" : decision.ReviewReason,
                decision.Term?.StartsAt, decision.Term?.EndsAt);
        }, ct);

    public Task<ErrorOr<ManualPaymentOutcome>> ConfirmAsync(AdminSessionCaller caller, Guid account, Guid order,
        ConfirmManualPayment? command, CancellationToken ct) => store.InManualScopeAsync<ManualPaymentOutcome>(
        caller, account, order, true, true, (mfa, token) => ConfirmLockedAsync(caller, account, order, command, mfa, token), ct);

    public Task<ErrorOr<ManualPaymentReceipt>> RecoverAsync(AdminSessionCaller caller, Guid account, Guid order,
        Guid key, CancellationToken ct) => store.InManualScopeAsync<ManualPaymentReceipt>(caller, account, order,
        true, false, async (_, token) =>
        {
            var prior = await store.FindManualAsync(account, key, true, token);
            return prior is not null && prior.OrderId == order && prior.Receipt.AdminId == caller.AdminId
                ? prior.Receipt : ManualPaymentErrors.OperationMissing;
        }, ct);

    public Task<ErrorOr<ManualPaymentEvidenceDetails>> EvidenceAsync(AdminSessionCaller caller, Guid account, Guid order,
        Guid receipt, CancellationToken ct) => store.InManualScopeAsync<ManualPaymentEvidenceDetails>(caller, account, order,
        true, false, async (_, token) =>
        {
            var stored = await store.FindManualAsync(account, receipt, false, token);
            return stored?.OrderId == order ? evidence.Read(account, stored) : ManualPaymentErrors.OperationMissing;
        }, ct);

    private async Task<ErrorOr<ManualPaymentOutcome>> ConfirmLockedAsync(AdminSessionCaller caller, Guid account,
        Guid order, ConfirmManualPayment? command, DateTimeOffset mfa, CancellationToken ct)
    {
        if (command is null || command.OperationKey == Guid.Empty) return ManualPaymentErrors.Invalid;
        if (await store.FindManualAsync(account, command.OperationKey, true, ct) is { } prior)
        {
            if (prior.OrderId != order || prior.Receipt.AdminId != caller.AdminId || !evidence.Matches(account, prior, command))
                return ManualPaymentErrors.PayloadMismatch;
            if (prior.Receipt.ReviewReason == "RestorationPending")
            {
                await store.UseManualReceiptAsync(prior.Receipt.ReceiptId, ct);
                await fulfilment.RetryLockedAsync(account, order, null, prior.Receipt.ReceiptId, ct);
            }
            return new ManualPaymentOutcome(prior.Receipt, false);
        }
        if (!command.Verified) return ManualPaymentErrors.Invalid;
        var input = await ValidateAsync(account, order, command, ct);
        if (input.IsError) return input.Errors;

        var (quote, receiving, digest) = input.Value;
        var decision = await fulfilment.DecideAsync(quote, BillingAccountRules.StorageInstant(command.ReceivedAt), ct);
        var id = Guid.NewGuid();
        var receipt = new ManualPaymentReceipt(id, command.OperationKey, caller.AdminId, mfa,
            BillingAccountRules.StorageInstant(command.ReceivedAt), BillingAccountRules.StorageInstant(clock.GetUtcNow()),
            quote.AmountPaise, quote.Currency, receiving.Id, receiving.ConfigurationVersion, receiving.EvidencePolicyVersion,
            receiving.Environment, decision.Reduction is not null ? "ReductionPending"
                : decision.Increase is not null ? "CapacityPending" : decision.ReviewReason, decision.Term?.TermId);
        await store.SaveManualAsync(account, order, caller, command, receipt, digest,
            evidence.Protect(account, order, id, command), ct);
        if (decision.Increase is { } change)
            await increases.FundAsync(change, null, id, ct);
        else if (decision.Reduction is { } funding)
            await reductions.FundAsync(funding, null, id, receipt.VerifiedAt, ct);
        else if (decision.Term is null)
            await reviews.RecordManualAsync(account, order, receipt, ct);
        if (decision.Hold is { } hold)
            await payments.RecordRestorationHoldAsync(account, order, null, id, receipt.ReceivedAt, hold, ct);
        if (decision.Term is not null)
            await fulfilment.FulfilAsync(quote, decision, null, id, ct);
        await confirmations.IssueAsync(account, order, null, id, receipt.VerifiedAt, ct);
        return new ManualPaymentOutcome(receipt, true);
    }

    private async Task<ErrorOr<(CheckoutQuoteV1 Quote, ManualReceivingAccount Receiving, string Digest)>> ValidateAsync(
        Guid account, Guid order, ConfirmManualPayment? command, CancellationToken ct)
    {
        if (command is null || !validator.Validate(command).IsValid || command.ReceivedAt > clock.GetUtcNow())
            return ManualPaymentErrors.Invalid;
        if (await AcceptedOrderAsync(account, order, ct) is not { } quote) return ManualPaymentErrors.Missing;
        if (quote.Revision != command.ExpectedRevision || quote.SnapshotHash != command.SnapshotHash)
            return CheckoutOrderErrors.Changed;
        if (quote.AmountPaise != command.AmountPaise || quote.Currency != command.Currency) return ManualPaymentErrors.Invalid;
        if (evidence.Account is not { } receiving) return ManualPaymentErrors.Unavailable;
        if (evidence.ReferenceDigest(command) is not { } digest) return ManualPaymentErrors.Invalid;
        return (quote, receiving, digest);
    }

    private async Task<CheckoutQuoteV1?> AcceptedOrderAsync(Guid account, Guid order, CancellationToken ct) =>
        (await orders.FindAsync(account, order, false, ct))?.Quote is { Acceptance: { } acceptance } quote &&
        CheckoutPaymentStatusReader.Startable(quote) && acceptance.AcceptedAt < quote.ExpiresAt ? quote : null;

    private async Task<ManualPaymentOrder> OrderViewAsync(CheckoutQuoteV1 quote, Guid? after, CancellationToken ct)
    {
        var shops = await orders.PageAsync(quote.AccountId, quote.OrderId, after, ct);
        return new(quote.AccountId, quote.OrderId, quote.Revision, quote.SnapshotHash, quote.PlanName, quote.Period,
            quote.AmountPaise, quote.Currency, quote.Acceptance!.AcceptedAt, quote.ShopCount, shops.Items, shops.NextCursor);
    }
}
