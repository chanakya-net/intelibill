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
    internal async Task<ErrorOr<ShopClosureRequestV1>> ConfirmAsync(MerchantBillingCaller caller, Guid shop,
        ShopClosureConfirmationV1 input, (Guid Id, string Digest, ClosureTargetV1 Target)? proposal, CancellationToken ct)
    {
        if (!input.IsValid())
            return Error.Validation("Closure.AcknowledgementRequired", "Review the outstanding work and consider disconnected device queues.");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var entered = await EnterAsync(caller, shop, ct);
            if (entered.IsError)
                return entered.Errors;

            var result = await RecordConsentAsync(entered.Value, input, proposal, ct);
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

    private async Task<ErrorOr<ShopClosureRequestV1>> RecordConsentAsync(ClosureAccess access, ShopClosureConfirmationV1 input,
        (Guid Id, string Digest, ClosureTargetV1 Target)? proposal, CancellationToken ct)
    {
        var binding = access.Binding;
        var owner = access.Owner;
        var proposalId = proposal?.Id ?? input.OperationKey;
        var isAccountClosure = proposal.HasValue;
        var prior = await ConsentRows(binding.ShopId, binding.AccountId, proposalId)
            .SingleOrDefaultAsync(row => row.OperationKey == input.OperationKey, ct);
        var target = proposal?.Target ?? new ClosureTargetV1(binding.ShopId, binding.AffiliationRequestId,
            input.ExpectedVersion, prior?.ShopName ?? owner.ShopName);
        var scopeDigest = proposal?.Digest ?? SignedServiceAssertion.Digest(JsonSerializer.SerializeToUtf8Bytes(new[] { target }, Json));
        var result = new ShopClosureRequestV1(binding.AccountId, target, input.OperationKey, input.WorkDigest);

        if (target.ShopId != binding.ShopId || target.AffiliationRequestId != binding.AffiliationRequestId ||
            target.ClosureVersion != input.ExpectedVersion || owner.Version != input.OwnerVersion)
            return Stale;
        if (prior is not null)
            return prior.ActorId == owner.ActorId && prior.MembershipId == owner.MembershipId &&
                prior.OwnerVersion == owner.Version && prior.IsAccountClosure == isAccountClosure &&
                prior.ScopeDigest == scopeDigest && prior.WorkDigest == input.WorkDigest
                ? result : MerchantBillingErrors.PayloadMismatch;

        var review = await ReadReviewAsync(access, ct);
        if (review.ClosureVersion != input.ExpectedVersion || review.WorkDigest != input.WorkDigest)
            return Stale;

        await InsertConsentAsync(review, owner, proposalId, isAccountClosure, input.OperationKey, scopeDigest, ct);
        return result;
    }

    private Task<int> InsertConsentAsync(ShopClosureReviewV1 review, ShopAffiliationConsentStore.LiveOwner owner,
        Guid proposal, bool isAccountClosure, Guid operation, string digest, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO public.shop_closure_consents
            (id, shop_id, account_id, affiliation_id, proposal_id, is_account_closure, operation_key, actor_id, membership_id,
             owner_version, closure_version, scope_digest, work_digest, evidence, recorded_at)
        VALUES ({Guid.NewGuid()}, {review.ShopId}, {review.AccountId}, {review.AffiliationRequestId}, {proposal},
            {isAccountClosure}, {operation}, {owner.ActorId}, {owner.MembershipId}, {owner.Version}, {review.ClosureVersion},
            {digest}, {review.WorkDigest}, {JsonSerializer.Serialize(review, Json)}::jsonb, clock_timestamp())
        """, ct);

    private IQueryable<ConsentRow> ConsentRows(Guid shop, Guid account, Guid proposal) => db.Database.SqlQuery<ConsentRow>($"""
        SELECT id, affiliation_id, operation_key, actor_id, membership_id, owner_version, closure_version, is_account_closure,
            scope_digest, work_digest, evidence->>'shopName' AS shop_name, recorded_at FROM public.shop_closure_consents
        WHERE shop_id = {shop} AND account_id = {account} AND proposal_id = {proposal}
        """);

    private static Error Stale => Error.Conflict("Closure.ReviewChanged", "The closure review changed. Review the shop again.");

    private sealed record ConsentRow(Guid Id, Guid AffiliationId, Guid OperationKey, Guid ActorId, Guid MembershipId,
        long OwnerVersion, long ClosureVersion, bool IsAccountClosure, string ScopeDigest, string WorkDigest,
        string ShopName, DateTimeOffset RecordedAt);
}
