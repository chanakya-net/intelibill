using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Entitlements;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Restrictions;

internal sealed partial class ClosureStore(LicensingDbContext db, EntitlementPublisher publisher) : IClosureStore
{
    public Task LockAccountAsync(Guid account, CancellationToken ct) => BillingAccountLock.TakeAsync(db, account, ct);

    public Task<ClosureTargetV1[]> TargetsAsync(Guid account, Guid? shop, CancellationToken ct) =>
        db.Database.SqlQuery<ClosureTargetV1>($"""
            SELECT a.shop_id, a.request_id AS affiliation_request_id, a.shop_name,
                CASE WHEN EXISTS (SELECT FROM public.operational_closure_targets t
                    WHERE t.account_id = a.account_id AND t.shop_id = a.shop_id AND t.affiliation_request_id = a.request_id)
                    THEN 1::bigint ELSE 0::bigint END AS closure_version
            FROM public.shop_affiliations a
            WHERE a.account_id = {account} AND ({shop}::uuid IS NULL OR a.shop_id = {shop})
            ORDER BY a.shop_id LIMIT {ShopClosuresV1.MaxTargets + 1}
            """).ToArrayAsync(ct);

    public Task<SavedClosure?> ReadOperationAsync(Guid account, Guid operation, CancellationToken ct) =>
        db.Database.SqlQuery<SavedClosure>($"""
            SELECT id, account_id, actor_id, shop_id, operation_key, request_digest, recorded_at
            FROM public.operational_closures WHERE account_id = {account} AND operation_key = {operation}
            """).SingleOrDefaultAsync(ct);

    public async Task<ClosureSavedV1> ReadSavedAsync(SavedClosure saved, CancellationToken ct)
    {
        var targets = await db.Database.SqlQuery<ClosureTargetV1>($"""
            SELECT shop_id, affiliation_request_id, shop_name, 1::bigint AS closure_version
            FROM public.operational_closure_targets WHERE closure_id = {saved.Id} ORDER BY shop_id
            """).ToArrayAsync(ct);
        return new(saved.Id, saved.AccountId, saved.OperationKey, saved.ShopId is null ? "Account" : "Shop", saved.RecordedAt,
            ShopClosuresV1.FinancialTreatment, targets);
    }

    public async Task<ClosureSavedV1> SaveAsync(SavedClosure saved, ClosureTargetV1[] targets,
        ClosureValidatedV1 consent, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.operational_closures
                (id, account_id, actor_id, shop_id, operation_key, request_digest, financial_treatment, recorded_at)
            VALUES ({saved.Id}, {saved.AccountId}, {saved.ActorId}, {saved.ShopId}, {saved.OperationKey},
                {saved.RequestDigest}, {ShopClosuresV1.FinancialTreatment}, {saved.RecordedAt})
            """, ct);
        foreach (var target in targets)
            await PublishTargetAsync(saved, target, consent.Targets.Single(proof => proof.ShopId == target.ShopId), ct);
        return await ReadSavedAsync(saved, ct);
    }

    private async Task PublishTargetAsync(SavedClosure saved, ClosureTargetV1 target, ClosureValidatedTargetV1 consent,
        CancellationToken ct)
    {
        var publication = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.operational_closure_targets (closure_id, account_id, shop_id, affiliation_request_id,
                shop_name, consent_id, work_digest, event_id)
            VALUES ({saved.Id}, {saved.AccountId}, {target.ShopId}, {target.AffiliationRequestId},
                {target.ShopName}, {consent.ConsentId}, {consent.WorkDigest}, {publication})
            """, ct);
        await publisher.PublishClosureAsync(saved.AccountId, target.ShopId, publication, ct);
    }
}
