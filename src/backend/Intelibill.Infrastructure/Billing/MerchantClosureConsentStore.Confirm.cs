using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Billing;

internal sealed partial class MerchantClosureConsentStore
{
    internal Task<ErrorOr<RecordedClosureConsent>> ConfirmAsync(MerchantBillingCaller caller, Guid shop,
        ShopClosureConfirmationV1 input, (Guid Id, string Digest, ClosureTargetV1 Target)? proposal, CancellationToken ct) =>
        WithConsentAccessAsync(caller, shop, input, access => RecordConsentAsync(access, input, proposal, ct), ct);

    private async Task<ErrorOr<T>> WithConsentAccessAsync<T>(MerchantBillingCaller caller, Guid shop, ShopClosureConfirmationV1 input,
        Func<ClosureAccess, Task<ErrorOr<T>>> action, CancellationToken ct)
    {
        if (!input.IsValid())
            return Error.Validation("Closure.AcknowledgementRequired", "Review the outstanding work and consider disconnected device queues.");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var entered = await EnterAsync(caller, shop, ct);
            if (entered.IsError)
                return entered.Errors;

            var result = await action(entered.Value);
            if (!result.IsError)
                await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return MerchantBillingErrors.Unavailable;
        }
    }

    private async Task<ErrorOr<RecordedClosureConsent>> RecordConsentAsync(ClosureAccess access, ShopClosureConfirmationV1 input,
        (Guid Id, string Digest, ClosureTargetV1 Target)? proposal, CancellationToken ct)
    {
        var binding = access.Binding;
        var owner = access.Owner;
        var proposalId = proposal?.Id ?? input.OperationKey;
        var isAccountClosure = proposal.HasValue;
        var prior = await ConsentRows(binding.ShopId)
            .SingleOrDefaultAsync(row => row.OperationKey == input.OperationKey, ct);
        var target = proposal?.Target ?? new ClosureTargetV1(binding.ShopId, binding.AffiliationRequestId,
            input.ExpectedVersion, prior?.ShopName ?? owner.ShopName);
        var scopeDigest = proposal?.Digest ?? SignedServiceAssertion.Digest(JsonSerializer.SerializeToUtf8Bytes(new[] { target }, Json));
        var request = new ShopClosureRequestV1(binding.AccountId, target, input.OperationKey, input.WorkDigest);

        var currentBinding = target.ShopId == binding.ShopId && target.AffiliationRequestId == binding.AffiliationRequestId &&
            target.ClosureVersion == input.ExpectedVersion && owner.Version == input.OwnerVersion;
        if (prior is not null)
            return MatchesRequest(prior, access, input, proposalId, isAccountClosure) &&
                prior.ScopeDigest == scopeDigest
                ? new RecordedClosureConsent(prior.Id, request) : MerchantBillingErrors.PayloadMismatch;
        if (!currentBinding)
            return Stale;

        var review = await ReadReviewAsync(access, ct);
        if (review.ClosureVersion != input.ExpectedVersion || review.WorkDigest != input.WorkDigest)
            return Stale;

        var consentId = Guid.NewGuid();
        await InsertConsentAsync(consentId, review, owner, proposalId, isAccountClosure, input.OperationKey, scopeDigest, ct);
        return new RecordedClosureConsent(consentId, request);
    }

    private static bool MatchesRequest(ConsentRow row, ClosureAccess access, ShopClosureConfirmationV1 input,
        Guid proposal, bool accountClosure) =>
        row.ActorId == access.Owner.ActorId && row.AccountId == access.Binding.AccountId &&
        row.AffiliationId == access.Binding.AffiliationRequestId && row.ProposalId == proposal &&
        row.IsAccountClosure == accountClosure && row.ClosureVersion == input.ExpectedVersion &&
        row.OwnerVersion == input.OwnerVersion && row.WorkDigest == input.WorkDigest;

    internal Task<ErrorOr<OwnerClosureNoticeV1?>> FindRecordedNoticeAsync(MerchantBillingCaller caller, Guid shop,
        Guid proposal, ShopClosureConfirmationV1 input, CancellationToken ct) =>
        WithConsentAccessAsync<OwnerClosureNoticeV1?>(caller, shop, input, async access =>
        {
            var row = await ConsentRows(shop).SingleOrDefaultAsync(row => row.OperationKey == input.OperationKey, ct);
            if (row is null) return (OwnerClosureNoticeV1?)null;
            if (!MatchesRequest(row, access, input, proposal, true))
                return MerchantBillingErrors.PayloadMismatch;
            return new OwnerClosureNoticeV1(row.AccountId, row.AffiliationId, proposal, row.ScopeDigest, row.OperationKey,
                row.OwnerVersion != access.Owner.Version || row.MembershipId != access.Owner.MembershipId, row.Id);
        }, ct);

    private Task<int> InsertConsentAsync(Guid consentId, ShopClosureReviewV1 review, ShopAffiliationConsentStore.LiveOwner owner,
        Guid proposal, bool isAccountClosure, Guid operation, string digest, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO public.shop_closure_consents
            (id, shop_id, account_id, affiliation_id, proposal_id, is_account_closure, operation_key, actor_id, membership_id,
             owner_version, closure_version, scope_digest, work_digest, evidence, recorded_at)
        VALUES ({consentId}, {review.ShopId}, {review.AccountId}, {review.AffiliationRequestId}, {proposal},
            {isAccountClosure}, {operation}, {owner.ActorId}, {owner.MembershipId}, {owner.Version}, {review.ClosureVersion},
            {digest}, {review.WorkDigest}, {JsonSerializer.Serialize(review, Json)}::jsonb, clock_timestamp())
        """, ct);

    private IQueryable<ConsentRow> ConsentRows(Guid shop) => db.Database.SqlQuery<ConsentRow>($"""
        SELECT id, account_id, proposal_id, affiliation_id, operation_key, actor_id, membership_id, owner_version, closure_version, is_account_closure,
            scope_digest, work_digest, evidence->>'shopName' AS shop_name, recorded_at FROM public.shop_closure_consents
        WHERE shop_id = {shop}
        """);

    private static Error Stale => Error.Conflict(ShopClosuresV1.ReviewChangedCode, "The closure review changed. Review the shop again.");

    internal sealed record RecordedClosureConsent(Guid Id, ShopClosureRequestV1 Request);

    private sealed record ConsentRow(Guid Id, Guid AccountId, Guid ProposalId, Guid AffiliationId, Guid OperationKey, Guid ActorId, Guid MembershipId,
        long OwnerVersion, long ClosureVersion, bool IsAccountClosure, string ScopeDigest, string WorkDigest,
        string ShopName, DateTimeOffset RecordedAt);
}
