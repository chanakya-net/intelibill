using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Restrictions;

public sealed partial class CloseShopHandler
{
    public Task<ErrorOr<AccountClosureProposalV1>> ProposeAsync(VerifiedBillingDelegation proof, Guid account,
        ProposeAccountClosureV1 input, CancellationToken ct) =>
        access.ExecuteAsync<AccountClosureProposalV1>(proof, async (scope, token) =>
        {
            if (input.OperationKey == Guid.Empty || input.FinancialTreatment != ShopClosuresV1.FinancialTreatment)
                return Error.Validation("Closure.AcknowledgementRequired", "Confirm separate financial treatment.");
            var revision = await store.BillingOwnerRevisionAsync(account, scope.ActorId, token);
            if (revision is null) return BillingDelegationErrors.Denied;
            var now = EntitlementSnapshotsV1.Instant(clock);
            var prior = await store.ProposalByOperationAsync(account, input.OperationKey, token);
            if (prior is not null)
                return prior.ActorId == scope.ActorId && prior.BillingRevision == revision
                    ? await ViewAsync(prior, token) : Stale;

            if (await store.AccountClosedAsync(account, token))
                return Error.Conflict("Closure.AccountClosed", "Operational closure is already saved for this billing account.");
            var targets = await store.TargetsAsync(account, null, token);
            if (targets.Length > ShopClosuresV1.MaxTargets)
                return Error.Conflict("Closure.ScopeTooLarge", "The complete account scope exceeds the closure review limit.");
            if (targets.Length == 0)
                return Stale;
            var proposal = new SavedClosureProposal(Guid.NewGuid(), account, input.OperationKey, scope.ActorId, revision.Value,
                Digest(targets), targets, now, now.AddDays(1));
            await store.InsertProposalAsync(proposal, token);
            return await ViewAsync(proposal, token);
        }, ct);

    public Task<ErrorOr<AccountClosureStateV1>> ReadAccountStateAsync(VerifiedBillingDelegation proof, Guid account,
        CancellationToken ct) => access.ExecuteAsync<AccountClosureStateV1>(proof, async (scope, token) =>
    {
        var revision = await store.BillingOwnerRevisionAsync(account, scope.ActorId, token);
        if (revision is null) return BillingDelegationErrors.Denied;
        var closed = await store.ReadAccountClosureAsync(account, token);
        if (closed is not null)
            return new AccountClosureStateV1(null, closed);
        var proposal = await store.ProposalAsync(account, null, clock.GetUtcNow(), token);
        return proposal is not null && proposal.ActorId == scope.ActorId && proposal.BillingRevision == revision
            ? new AccountClosureStateV1(await ViewAsync(proposal, token), null) : Missing;
    }, ct);

    public Task<ErrorOr<ClosureSavedV1>> ConfirmAccountAsync(VerifiedBillingDelegation proof, Guid account, Guid proposalId,
        ConfirmAccountClosureV1 input, CancellationToken ct) => access.ExecuteAsync<ClosureSavedV1>(proof, async (scope, token) =>
    {
        if (!input.Confirmed || input.OperationKey == Guid.Empty || !ShopClosuresV1.IsDigest(input.ScopeDigest))
            return Error.Validation("Closure.AcknowledgementRequired", "Confirm the exact billing account closure scope.");
        var revision = await store.BillingOwnerRevisionAsync(account, scope.ActorId, token);
        if (revision is null) return BillingDelegationErrors.Denied;
        var digest = Digest(new { proposalId, input });
        var saved = await store.ReadOperationAsync(account, input.OperationKey, token);
        if (saved is not null)
            return saved.ActorId == scope.ActorId && saved.ShopId is null && saved.RequestDigest == digest
                ? await store.ReadSavedAsync(saved, token) : Stale;

        var proposal = await store.ProposalAsync(account, proposalId, clock.GetUtcNow(), token);
        if (proposal is null || proposal.ActorId != scope.ActorId || proposal.BillingRevision != revision ||
            proposal.ScopeDigest != input.ScopeDigest)
            return Stale;
        if (clock.GetUtcNow() >= proposal.ExpiresAt || await store.AccountClosedAsync(account, token) ||
            !(await store.TargetsAsync(account, null, token)).SequenceEqual(proposal.Targets))
            return Stale;
        var deadline = proposal.ExpiresAt < proof.ExpiresAt ? proposal.ExpiresAt : proof.ExpiresAt;
        var validation = await RequireConsentAsync(account, proposalId, proposal.Targets, null, false, deadline, token);
        if (validation.IsError) return validation.Errors;
        return await store.SaveAsync(new(Guid.NewGuid(), account, scope.ActorId, null, input.OperationKey,
            digest, EntitlementSnapshotsV1.Instant(clock)), proposal.Targets, validation.Value, token);
    }, ct);

    public Task<ErrorOr<OwnerClosureProposalV1>> ReadOwnerProposalAsync(VerifiedBillingDelegation proof, Guid account, Guid shop,
        Guid affiliation, CancellationToken ct) => access.ExecuteAsync<OwnerClosureProposalV1>(proof, async (scope, token) =>
    {
        if (account == Guid.Empty || shop == Guid.Empty || affiliation == Guid.Empty) return Missing;
        await scope.UseAccountAsync(account, token);
        return await ReadOwnerProposalAsync(account, shop, affiliation, null, token);
    }, ct);

    public Task<ErrorOr<OwnerClosureProposalV1>> RecordNoticeAsync(VerifiedBillingDelegation proof, Guid shop,
        OwnerClosureNoticeV1 notice, CancellationToken ct) => access.ExecuteAsync<OwnerClosureProposalV1>(proof, async (scope, token) =>
    {
        if (notice.AccountId == Guid.Empty || notice.ProposalId == Guid.Empty) return Missing;
        await scope.UseAccountAsync(notice.AccountId, token);
        await store.LockAccountAsync(notice.AccountId, token);
        if (clock.GetUtcNow() >= proof.ExpiresAt) return BillingDelegationErrors.Invalid;
        var recorded = notice.OperationKey is { } key
            ? await store.FindConsentOutcomeAsync(notice, shop, scope.ActorId, key, token) : null;
        if (recorded == true)
        {
            var affiliated = await store.TargetsAsync(notice.AccountId, shop, token);
            if (affiliated is not [var current] || current.AffiliationRequestId != notice.AffiliationRequestId)
                return Missing;
            var saved = await store.ProposalAsync(notice.AccountId, notice.ProposalId, clock.GetUtcNow(), token);
            var target = saved?.Targets.SingleOrDefault(row => row.ShopId == shop && row.AffiliationRequestId == notice.AffiliationRequestId);
            return saved is not null && target is not null && saved.ScopeDigest == notice.ScopeDigest
                ? new OwnerClosureProposalV1(saved.Id, saved.AccountId, saved.ScopeDigest, saved.ExpiresAt, saved.Targets.Length, target, true)
                : Stale;
        }
        // A legacy shop-level notice cannot establish which operation reached licensing.
        if (notice.RecoverOnly) return recorded == false ? Stale : Missing;
        var proposal = await ReadOwnerProposalAsync(notice.AccountId, shop, notice.AffiliationRequestId, notice.ProposalId, token);
        if (proposal.IsError)
            return recorded == false && proposal.FirstError == Missing ? Stale : proposal.Errors;
        if (proposal.Value.ScopeDigest != notice.ScopeDigest) return Stale;
        await store.RecordNoticeAsync(notice.AccountId, notice.ProposalId, shop, scope.ActorId, notice.OperationKey, token);
        return proposal.Value with { ConsentRecorded = true };
    }, ct);

    private async Task<ErrorOr<OwnerClosureProposalV1>> ReadOwnerProposalAsync(Guid account, Guid shop, Guid affiliation,
        Guid? id, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var proposal = await store.ProposalAsync(account, id, now, ct);
        var target = proposal?.Targets.SingleOrDefault(row => row.ShopId == shop && row.AffiliationRequestId == affiliation);
        if (proposal is null || target is null || proposal.ExpiresAt <= now || await store.AccountClosedAsync(account, ct))
            return Missing;
        if ((await store.TargetsAsync(account, shop, ct)).SingleOrDefault() != target)
            return Stale;
        var notices = await store.ReadNoticesAsync(account, proposal.Id, ct);
        return new OwnerClosureProposalV1(proposal.Id, account, proposal.ScopeDigest, proposal.ExpiresAt, proposal.Targets.Length,
            target, notices.Contains(shop));
    }

    private async Task<AccountClosureProposalV1> ViewAsync(SavedClosureProposal item, CancellationToken ct)
    {
        var notices = await store.ReadNoticesAsync(item.AccountId, item.Id, ct);
        return new(item.Id, item.AccountId, item.OperationKey, item.ScopeDigest, item.ExpiresAt, ShopClosuresV1.FinancialTreatment,
            [.. item.Targets.Select(target => new ClosureProposalShopV1(target.ShopId, target.ShopName, notices.Contains(target.ShopId)))]);
    }

    private static Error Missing => Error.NotFound("Closure.ProposalUnavailable", "No current account closure proposal is available.");
}
