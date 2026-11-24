using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Payments;

public sealed class AcceptCheckoutOrderHandler(IBillingDelegationAccess access, ICheckoutOrderStore store, TimeProvider clock)
{
    public Task<ErrorOr<CheckoutQuoteV1>> HandleAsync(VerifiedBillingDelegation proof, Guid account, Guid order,
        AcceptCheckoutQuoteV1 request, CancellationToken ct) =>
        access.ExecuteAsync<CheckoutQuoteV1>(proof, async (_, token) =>
        {
            var hash = CheckoutOrdersV1.AcceptHash(order, request);
            var replay = await store.FindAcceptanceAsync(account, request.OperationKey, token);
            if (replay is not null && (replay.ActorId != proof.ActorId || replay.OrderId != order || replay.RequestHash != hash))
                return BillingAccountErrors.PayloadMismatch;
            if (!request.IsValid())
                return CheckoutOrderErrors.Invalid;

            var retained = await store.FindAsync(account, order, false, token);
            if (retained is null)
                return CheckoutOrderErrors.Missing;
            var quote = retained.Quote;
            if (quote.Increase is not null && !await store.IsIncreaseOwnerAsync(account, order, proof.ActorId, token))
                return BillingDelegationErrors.Denied;
            if (replay is not null)
                return quote;
            if (quote.Acceptance is not null)
                return CheckoutOrderErrors.Accepted;
            if (!MatchesReview(quote, request))
                return CheckoutOrderErrors.Changed;
            if (Now() >= quote.ExpiresAt)
                return CheckoutOrderErrors.Expired;

            var ids = quote.TermId is null ? await store.ShopIdsAsync(account, order, token) : [];
            var source = await store.SourceAsync(account, quote.TermId, ids, token);
            if (source?.Matches(quote) != true || !await store.IncreaseStillCurrentAsync(quote, Now(), token))
                return CheckoutOrderErrors.Changed;
            var now = Now();
            if (now >= quote.ExpiresAt || !CheckoutRenewalPolicy.WindowOpen(quote, now))
                return CheckoutOrderErrors.Expired;
            if (now < quote.CreatedAt)
                return CheckoutOrderErrors.Changed;

            var acceptance = new CheckoutAcceptanceV1(request.OperationKey, quote.Revision, quote.SnapshotHash, now);
            return await store.AcceptAsync(quote, acceptance, proof.ActorId, proof.RequestId, proof.ReplayId, hash, token) == 1
                ? quote with { Acceptance = acceptance } : BillingDelegationErrors.Denied;
        }, ct);

    private DateTimeOffset Now() => BillingAccountRules.StorageInstant(clock.GetUtcNow());

    private static bool MatchesReview(CheckoutQuoteV1 quote, AcceptCheckoutQuoteV1 request) =>
        quote.Revision == request.ExpectedRevision && quote.SnapshotHash == request.SnapshotHash &&
        quote.CustomerTermsVersion == request.CustomerTermsVersion && quote.RefundPolicyVersion == request.RefundPolicyVersion;
}
