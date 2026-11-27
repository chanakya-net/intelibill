using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantBillingClient
{
    public Task<ErrorOr<AccountClosureStateV1>> ReadAccountStateAsync(MerchantBillingCaller caller, Guid account, CancellationToken ct) =>
        ReadClosureAsync<AccountClosureStateV1>(caller, new(ShopClosuresV1.AccountRead, HttpMethod.Get,
            ShopClosuresV1.AccountPath(account), account, []), value => ValidAccountState(value, account), ct);

    public Task<ErrorOr<AccountClosureProposalV1>> ProposeAsync(MerchantBillingCaller caller, Guid account,
        ProposeAccountClosureV1 input, CancellationToken ct) => ReadClosureAsync<AccountClosureProposalV1>(caller,
            new(ShopClosuresV1.Propose, HttpMethod.Post, ShopClosuresV1.AccountPath(account), account,
                JsonSerializer.SerializeToUtf8Bytes(input, Json)),
            value => value.OperationKey == input.OperationKey && ValidProposal(value, account), ct);

    public Task<ErrorOr<ClosureSavedV1>> ConfirmAccountAsync(MerchantBillingCaller caller, Guid account, Guid proposal,
        ConfirmAccountClosureV1 input, CancellationToken ct) => ReadClosureAsync<ClosureSavedV1>(caller,
            new(ShopClosuresV1.Confirm, HttpMethod.Post, ShopClosuresV1.ConfirmPath(account, proposal), account,
                JsonSerializer.SerializeToUtf8Bytes(input, Json)),
            saved => saved.AccountId == account && saved.OperationKey == input.OperationKey && saved.Scope == "Account" && saved.Shops is { Length: > 0 and <= ShopClosuresV1.MaxTargets }
                && saved.FinancialTreatment == ShopClosuresV1.FinancialTreatment, ct);

    public async Task<ErrorOr<OwnerClosureContext>> ReadOwnerProposalAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct)
    {
        var review = await closures.PreviewAsync(caller, shop, ct);
        if (review.IsError) return review.Errors;
        var result = await ReadOwnerProposalAsync(caller, review.Value, ct);
        return result.IsError ? result.Errors : new OwnerClosureContext(result.Value, review.Value.OwnerVersion);
    }

    public async Task<ErrorOr<OwnerClosureProposalV1>> RecordConsentAsync(MerchantBillingCaller caller, Guid shop, Guid proposal,
        ShopClosureConfirmationV1 input, CancellationToken ct)
    {
        var prior = await closures.FindRecordedNoticeAsync(caller, shop, proposal, input, ct);
        if (prior.IsError) return prior.Errors;
        if (prior.Value is { } recorded)
            return await SendNoticeAsync(caller, shop, input.ExpectedVersion, recorded, ct);

        var context = await ReadOwnerProposalAsync(caller, shop, ct);
        if (context.IsError) return context.Errors;
        var pending = context.Value.Proposal;
        if (pending.Id != proposal)
            return Error.Conflict("Closure.ReviewChanged", "The account closure proposal changed.");
        var record = await closures.ConfirmAsync(caller, shop, input, (pending.Id, pending.ScopeDigest, pending.Target), ct);
        if (record.IsError) return record.Errors;

        var notice = new OwnerClosureNoticeV1(pending.AccountId, pending.Target.AffiliationRequestId, proposal,
            pending.ScopeDigest, input.OperationKey, ConsentId: record.Value.Id);
        return await SendNoticeAsync(caller, shop, input.ExpectedVersion, notice, ct);
    }

    private Task<ErrorOr<OwnerClosureProposalV1>> SendNoticeAsync(MerchantBillingCaller caller, Guid shop, long version,
        OwnerClosureNoticeV1 notice, CancellationToken ct) => ReadClosureAsync<OwnerClosureProposalV1>(caller,
            new(ShopClosuresV1.OwnerNotice, HttpMethod.Post, ShopClosuresV1.NoticePath(shop), null,
                JsonSerializer.SerializeToUtf8Bytes(notice, Json)),
            value => value.Id == notice.ProposalId && value.AccountId == notice.AccountId && value.Target?.ShopId == shop &&
                value.Target.AffiliationRequestId == notice.AffiliationRequestId && value.Target.ClosureVersion == version &&
                value.ScopeDigest == notice.ScopeDigest && value.ConsentRecorded, ct);

    private Task<ErrorOr<OwnerClosureProposalV1>> ReadOwnerProposalAsync(MerchantBillingCaller caller, ShopClosureReviewV1 review,
        CancellationToken ct) => ReadClosureAsync<OwnerClosureProposalV1>(caller, new(ShopClosuresV1.OwnerProposal, HttpMethod.Get,
            ShopClosuresV1.OwnerPath(review.ShopId, review.AccountId, review.AffiliationRequestId), null, []),
            value => value.Id != Guid.Empty && value.AccountId == review.AccountId &&
                value.Target?.ShopId == review.ShopId && value.Target.AffiliationRequestId == review.AffiliationRequestId &&
                value.Target.ClosureVersion == review.ClosureVersion && ShopClosuresV1.IsDigest(value.ScopeDigest), ct);

    private static bool ValidAccountState(AccountClosureStateV1 value, Guid account) =>
        value.Closed is { } closed
            ? value.Proposal is null && closed.AccountId == account && EntitlementSnapshotsV1.IsInstant(closed.SavedAt) &&
                closed.FinancialTreatment == ShopClosuresV1.FinancialTreatment &&
                closed.Shops is { Length: > 0 and <= ShopClosuresV1.MaxTargets }
            : value.Proposal is { } proposal && ValidProposal(proposal, account);

    private static bool ValidProposal(AccountClosureProposalV1 value, Guid account) =>
        value.Id != Guid.Empty && value.OperationKey != Guid.Empty && value.AccountId == account &&
        ShopClosuresV1.IsDigest(value.ScopeDigest) &&
        value.FinancialTreatment == ShopClosuresV1.FinancialTreatment &&
        value.Shops is { Length: > 0 and <= ShopClosuresV1.MaxTargets };
}
