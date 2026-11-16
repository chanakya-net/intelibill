using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Trials;

public sealed class StartTrialHandler(ITrialStore store, ITrialPolicyReader policies, IBillingDelegationAccess access,
    TimeProvider clock)
{
    public Task<ErrorOr<ShopTrialPreviewV1>> PreviewAsync(VerifiedBillingDelegation proof, Guid shop, CancellationToken ct) =>
        access.ExecuteAsync<ShopTrialPreviewV1>(proof, (_, token) => PreviewInScopeAsync(shop, proof.ReplayId, token), ct);

    private async Task<ErrorOr<ShopTrialPreviewV1>> PreviewInScopeAsync(Guid shop, Guid replay, CancellationToken ct)
    {
        var affiliation = await store.UseShopAsync(shop, ct);
        var policy = await policies.ReadAsync(replay, ct);
        if (affiliation is { } bound)
            await store.UseAccountAsync(bound.AccountId, ct);
        var at = SubscriptionStatusProjection.EvaluationInstant(clock);
        var interval = Interval(at);
        var reason = TrialEligibilityPolicy.Check(affiliation, await store.HasHistoryAsync(shop, ct), policy);
        if (reason is null)
            reason = await CapacityReasonAsync(affiliation!, policy!, interval, ct);
        return new ShopTrialPreviewV1(shop, policy, reason is null, reason, at, interval.EndsAt);
    }

    public Task<ErrorOr<ShopTrialDeliveryResultV1>> ApplyAsync(ShopTrialConsentV1 consent, TrialDeliveryProof proof,
        CancellationToken ct) => store.InDeliveryAsync(consent, proof, (affiliation, token) =>
            ApplyInScopeAsync(consent, proof, affiliation, token), ct);

    private async Task<ErrorOr<ShopTrialDeliveryResultV1>> ApplyInScopeAsync(ShopTrialConsentV1 consent,
        TrialDeliveryProof proof, TrialAffiliation? affiliation, CancellationToken ct)
    {
        if (await store.ReadReceiptAsync(consent, ct) is { } replay)
        {
            return replay.EventId == consent.EventId && replay.Digest == proof.Digest
                ? new ShopTrialDeliveryResultV1(replay.EventId, replay.Status, replay.Reason)
                : Error.Conflict("Trial.PayloadMismatch", "This operation already has a different accepted payload.");
        }

        var policy = await policies.ReadAsync(null, ct);
        var now = SubscriptionStatusProjection.EvaluationInstant(clock);
        var interval = Interval(consent.AcceptedAt);
        var reason = TrialEligibilityPolicy.Check(affiliation, await store.HasHistoryAsync(consent.ShopId, ct), policy);
        if (reason is null)
        {
            reason = affiliation!.AppliedAt > consent.AcceptedAt ? "AffiliationRequired" :
                TrialEligibilityPolicy.CheckConsent(consent, policy!, interval, now) ??
                await CapacityReasonAsync(affiliation, policy!, interval, ct);
        }

        var result = new ShopTrialDeliveryResultV1(consent.EventId, reason is null ? "Applied" : "NeedsReview", reason);
        var history = reason is null ? History(consent, affiliation!, policy!, interval, proof.Digest, now) : null;
        await store.SaveAsync(consent, proof, result, affiliation, history, ct);
        return result;
    }

    private async Task<string?> CapacityReasonAsync(TrialAffiliation affiliation, ShopTrialPolicyV1 policy,
        TrialInterval interval, CancellationToken ct) =>
        TrialEligibilityPolicy.HasCapacity(await store.ReadOverlapsAsync(affiliation.AccountId, interval, ct),
            interval, policy.AccountTrialCap) ? null : "AccountCap";

    private static TrialInterval Interval(DateTimeOffset start) => new(start, start.AddDays(ShopTrialsV1.DurationDays));

    private static TrialHistory History(ShopTrialConsentV1 consent, TrialAffiliation affiliation, ShopTrialPolicyV1 policy,
        TrialInterval interval, string digest, DateTimeOffset now) =>
        new(consent.ShopId, affiliation.AccountId, Guid.NewGuid(), consent.EventId, consent.Acceptance.OperationKey,
            consent.ActorId, consent.MembershipId, consent.AuthorityVersion, policy.PolicyId, policy.PolicyRevision,
            policy.TermsVersion, policy.MemberCap, policy.AccountTrialCap, consent.ActiveMembers, interval.StartsAt,
            interval.EndsAt, now, digest, policy.DevelopmentFixture);
}
