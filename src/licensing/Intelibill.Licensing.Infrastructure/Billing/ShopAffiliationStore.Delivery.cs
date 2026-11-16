using ErrorOr;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class ShopAffiliationStore
{
    public async Task<ErrorOr<ShopAffiliationAcknowledgementV1>> ApplyAsync(ShopAffiliationDecisionV1 decision, string digest,
        Guid replayId, DateTimeOffset expiresAt, CancellationToken ct)
    {
        try
        {
            return await ApplyInTransactionAsync(decision, digest, replayId, expiresAt, ct);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return BillingDelegationErrors.StorageUnavailable;
        }
    }

    private async Task<ErrorOr<ShopAffiliationAcknowledgementV1>> ApplyInTransactionAsync(ShopAffiliationDecisionV1 decision,
        string digest, Guid replayId, DateTimeOffset expiresAt, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.billing_account_id', {decision.AccountId.ToString("D")}, true)
            """, ct);
        var request = await ReadDecisionRequestAsync(decision, ct);
        if (request is null || request.TermId != decision.TermId ||
            decision.AcceptedAt < request.CreatedAt || decision.AcceptedAt >= request.ExpiresAt || decision.AcceptedAt > Now)
            return ShopAffiliationPolicy.Conflict;

        await BillingAccountLock.TakeAsync(db, decision.AccountId, ct);
        var authorized = await LockRequesterAsync(decision.AccountId, request, ct);
        if (Now >= expiresAt || !await ConsumeDeliveryAsync(decision.AccountId, replayId, expiresAt, ct) || Now >= expiresAt)
            return BillingDelegationErrors.Replayed;

        var result = await ApplyOnceAsync(decision, digest, authorized, ct);
        if (result.IsError)
            return result.Errors;
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task<ErrorOr<ShopAffiliationAcknowledgementV1>> ApplyOnceAsync(ShopAffiliationDecisionV1 decision,
        string digest, bool authorized, CancellationToken ct)
    {
        var receipt = await db.Database.SqlQuery<DeliveryReceipt>($"""
            SELECT digest, status FROM public.shop_affiliation_inbox WHERE event_id = {decision.EventId}
            """).SingleOrDefaultAsync(ct);
        if (receipt is not null)
            return receipt.Digest == digest ? new ShopAffiliationAcknowledgementV1(decision.EventId, receipt.Status)
                : ShopAffiliationPolicy.Conflict;

        var now = Now;
        var eligible = authorized && (decision.TermId is not { } term || await CanAssignAsync(decision.AccountId, term, now, ct));
        var applied = eligible && await InsertAffiliationAsync(decision, now, ct);
        var status = applied ? "Applied" : "NeedsReview";
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.shop_affiliation_inbox (event_id, account_id, shop_id, request_id, digest, status)
            VALUES ({decision.EventId}, {decision.AccountId}, {decision.ShopId}, {decision.RequestId}, {digest}, {status})
            ON CONFLICT DO NOTHING
            """, ct);
        if (inserted == 1 && applied)
            await publisher.PublishAsync(decision.AccountId, decision.ShopId, ct);
        return inserted == 1 ? new ShopAffiliationAcknowledgementV1(decision.EventId, status) : ShopAffiliationPolicy.Conflict;
    }

    private async Task<bool> InsertAffiliationAsync(ShopAffiliationDecisionV1 decision, DateTimeOffset now, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.shop_affiliations (request_id, account_id, shop_id, term_id, event_id, actor_id, membership_id,
                authority_version, shop_name, owner_name, accepted_at, applied_at, coverage_ends_at)
            VALUES ({decision.RequestId}, {decision.AccountId}, {decision.ShopId}, {decision.TermId}, {decision.EventId},
                {decision.ActorId}, {decision.MembershipId}, {decision.AuthorityVersion}, {decision.ShopName}, {decision.OwnerName},
                {decision.AcceptedAt}, {now}, (SELECT ends_at FROM public.subscription_coverage_terms WHERE term_id = {decision.TermId}))
            ON CONFLICT DO NOTHING
            """, ct) == 1;

    private Task<DecisionRequest?> ReadDecisionRequestAsync(ShopAffiliationDecisionV1 item, CancellationToken ct) =>
        db.Database.SqlQuery<DecisionRequest>($"""
            SELECT requester_id, requester_revision, term_id, created_at, expires_at
            FROM public.shop_affiliation_requests
            WHERE account_id = {item.AccountId} AND request_id = {item.RequestId} AND shop_id = {item.ShopId}
            """).SingleOrDefaultAsync(ct);

    private async Task<bool> LockRequesterAsync(Guid account, DecisionRequest request, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.billing_actor_id', {request.RequesterId.ToString("D")}, true)
            """, ct);
        return (await db.Database.SqlQuery<int>($"""
            SELECT revision AS "Value" FROM public.billing_account_members
            WHERE account_id = {account} AND merchant_user_id = {request.RequesterId}
                AND status = 1 AND role IN (1, 2)
            FOR SHARE
            """).ToListAsync(ct)) is [var revision] && revision == request.RequesterRevision;
    }

    private async Task<bool> ConsumeDeliveryAsync(Guid account, Guid replayId, DateTimeOffset expiresAt, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.shop_affiliation_delivery_uses (replay_id, account_id, expires_at)
            VALUES ({replayId}, {account}, {expiresAt}) ON CONFLICT DO NOTHING
            """, ct) == 1;

    private sealed record DeliveryReceipt(string Digest, string Status);
    private sealed record DecisionRequest(Guid RequesterId, int RequesterRevision, Guid? TermId,
        DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
}
