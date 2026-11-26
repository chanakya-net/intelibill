using Intelibill.Licensing.Application.Restrictions;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Restrictions;

internal sealed partial class ClosureStore
{
    public Task<ReopeningTarget?> ReadReopeningTargetAsync(Guid account, Guid closure, Guid shop, CancellationToken ct) =>
        db.Database.SqlQuery<ReopeningTarget>($"""
            SELECT t.closure_id, t.shop_id, t.affiliation_request_id, t.shop_name,
                CASE WHEN c.shop_id IS NULL THEN 'Account' ELSE 'Shop' END AS closure_scope,
                c.recorded_at AS closed_at,
                (SELECT count(*) FROM public.operational_closure_targets others
                    WHERE others.account_id = t.account_id AND others.shop_id = t.shop_id)
                + (SELECT count(*) FROM public.operational_closure_reopenings others
                    WHERE others.account_id = t.account_id AND others.shop_id = t.shop_id) AS version,
                EXISTS (SELECT FROM public.operational_closure_reopenings r
                    WHERE r.closure_id = t.closure_id AND r.shop_id = t.shop_id) AS reopened,
                (SELECT count(*) FROM public.active_operational_closure_targets other
                    WHERE other.account_id = t.account_id AND other.shop_id = t.shop_id
                        AND other.affiliation_request_id = t.affiliation_request_id AND other.closure_id <> t.closure_id
                ) AS other_active_closures
            FROM public.operational_closure_targets t
            JOIN public.operational_closures c ON c.id = t.closure_id AND c.account_id = t.account_id
            JOIN public.current_shop_affiliations a ON a.account_id = t.account_id AND a.shop_id = t.shop_id
                AND a.request_id = t.affiliation_request_id
            WHERE t.account_id = {account} AND t.closure_id = {closure} AND t.shop_id = {shop}
            """).SingleOrDefaultAsync(ct);

    public Task<ReopeningOperation?> ReadReopeningOperationAsync(Guid account, Guid operation, CancellationToken ct) =>
        db.Database.SqlQuery<ReopeningOperation>($"""
            SELECT id, closure_id, shop_id, version, reopened_at, reopened_by, payload_digest
            FROM public.operational_closure_reopenings WHERE account_id = {account} AND operation_key = {operation}
            """).SingleOrDefaultAsync(ct);

    public Task<bool> UsedReopeningConsentAsync(Guid account, Guid shop, string reference, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.operational_closure_reopenings WHERE account_id = {account}
                AND shop_id = {shop} AND owner_consent_reference = {reference}) AS "Value"
            """).SingleAsync(ct);

    public async Task SaveReopeningAsync(NewReopening reopening, CancellationToken ct)
    {
        var decision = reopening.Decision;
        var command = reopening.Command;
        var coverage = reopening.Coverage;
        var publication = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.operational_closure_reopenings
                (id, closure_id, account_id, shop_id, affiliation_request_id, version, prior_revision, opens_shop, term_id, basis,
                 starts_at, paid_through, access_ends_at, impact_digest, owner_consent_action, owner_consent_user_id, owner_consent_at,
                 owner_consent_reference, owner_consent_evidence, reason, evaluated_at, reopened_at, reopened_by, session_id, mfa_verified_at,
                 operation_key, payload_digest, event_id)
            VALUES ({decision.Id}, {decision.ClosureId}, {reopening.AccountId}, {decision.ShopId}, {reopening.AffiliationRequestId},
                {decision.Version}, (SELECT revision FROM public.entitlement_shop_heads
                    WHERE shop_id = {decision.ShopId} AND account_id = {reopening.AccountId}),
                NOT EXISTS (SELECT FROM public.active_operational_closure_targets t
                    WHERE t.account_id = {reopening.AccountId} AND t.shop_id = {decision.ShopId}
                        AND t.affiliation_request_id = {reopening.AffiliationRequestId} AND t.closure_id <> {decision.ClosureId}),
                {coverage.TermId}, {coverage.Basis}, {coverage.StartsAt}, {coverage.PaidThrough}, {coverage.AccessEndsAt},
                {command.ImpactDigest}, 'ReopenShop', {command.OwnerConsentUserId}, {command.OwnerConsentAt},
                {command.OwnerConsentReference!.Trim()}, {command.OwnerConsentEvidence!.Trim()}, {command.Reason!.Trim()},
                {reopening.EvaluatedAt}, {decision.ReopenedAt}, {reopening.AdminId}, {reopening.SessionId}, {reopening.MfaAt}, {command.OperationKey},
                {reopening.PayloadDigest}, {publication})
            """, ct);
        await publisher.PublishReopeningAsync(reopening.AccountId, decision.ShopId, reopening.EvaluatedAt, publication, ct);
    }
}
