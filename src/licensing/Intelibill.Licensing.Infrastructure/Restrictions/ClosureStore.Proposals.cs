using System.Text.Json;
using Intelibill.Licensing.Application.Restrictions;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Restrictions;

internal sealed partial class ClosureStore
{
    public Task<bool> AccountClosedAsync(Guid account, CancellationToken ct) => AccountClosedAsync(db, account, ct);

    internal static Task<bool> AccountClosedAsync(Data.LicensingDbContext context, Guid account, CancellationToken ct) =>
        context.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.operational_closures WHERE account_id = {account} AND shop_id IS NULL) AS "Value"
            """).SingleAsync(ct);

    public async Task<int?> BillingOwnerRevisionAsync(Guid account, Guid actor, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<int>($"""
            SELECT revision AS "Value" FROM public.billing_account_members WHERE account_id = {account}
                AND merchant_user_id = {actor} AND role = {(int)BillingAccountRole.Owner}
                AND status = {(int)BillingAccountMemberStatus.Active}
            FOR SHARE
            """).ToListAsync(ct);
        return rows is [var revision] ? revision : null;
    }

    public Task<SavedClosureProposal?> ProposalAsync(Guid account, Guid? id, DateTimeOffset now, CancellationToken ct) =>
        ReadProposalAsync(ProposalRows(account).Where(row => id == null ? row.ExpiresAt > now : row.Id == id)
            .OrderByDescending(row => row.Ordinal), ct);

    public Task<SavedClosureProposal?> ProposalByOperationAsync(Guid account, Guid operation, CancellationToken ct) =>
        ReadProposalAsync(ProposalRows(account).Where(row => row.OperationKey == operation), ct);

    private IQueryable<ProposalRow> ProposalRows(Guid account) => db.Database.SqlQuery<ProposalRow>($"""
        SELECT id, ordinal, account_id, coalesce(operation_key, id) AS operation_key,
            actor_id, billing_revision, scope_digest, scope::text, recorded_at, expires_at
        FROM public.operational_closure_proposals WHERE account_id = {account}
        """);

    private static async Task<SavedClosureProposal?> ReadProposalAsync(IQueryable<ProposalRow> query, CancellationToken ct)
    {
        var row = await query.FirstOrDefaultAsync(ct);
        return row is null ? null : new(row.Id, row.AccountId, row.OperationKey, row.ActorId, row.BillingRevision, row.ScopeDigest,
            JsonSerializer.Deserialize<ClosureTargetV1[]>(row.Scope, EntitlementSnapshotsV1.Json)!, row.RecordedAt, row.ExpiresAt);
    }

    public Task InsertProposalAsync(SavedClosureProposal item, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO public.operational_closure_proposals
            (id, account_id, operation_key, actor_id, billing_revision, financial_treatment, scope_digest, scope, recorded_at, expires_at)
        VALUES ({item.Id}, {item.AccountId}, {item.OperationKey}, {item.ActorId}, {item.BillingRevision}, {ShopClosuresV1.FinancialTreatment},
            {item.ScopeDigest}, {JsonSerializer.Serialize(item.Targets, EntitlementSnapshotsV1.Json)}::jsonb,
            {item.RecordedAt}, {item.ExpiresAt})
        """, ct);

    public Task<Guid[]> ReadNoticesAsync(Guid account, Guid proposal, CancellationToken ct) => db.Database.SqlQuery<Guid>($"""
        SELECT DISTINCT shop_id AS "Value" FROM public.operational_closure_consent_notices
        WHERE account_id = {account} AND proposal_id = {proposal}
        """).ToArrayAsync(ct);

    public Task<bool?> FindNoticeAsync(Guid account, Guid proposal, Guid shop, Guid actor, Guid operation, CancellationToken ct) =>
        db.Database.SqlQuery<bool?>($"""
            SELECT CASE WHEN count(*) FILTER (WHERE actor_id = {actor} AND operation_key = {operation}) > 0 THEN true
                        WHEN count(*) FILTER (WHERE operation_key IS NULL) > 0 THEN NULL
                        ELSE false END AS "Value"
            FROM public.operational_closure_consent_notices
            WHERE account_id = {account} AND proposal_id = {proposal} AND shop_id = {shop}
            """).SingleAsync(ct);

    public Task RecordNoticeAsync(Guid account, Guid proposal, Guid shop, Guid actor, Guid? operation, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.operational_closure_consent_notices (proposal_id, account_id, shop_id, actor_id, operation_key)
            VALUES ({proposal}, {account}, {shop}, {(operation.HasValue ? actor : (Guid?)null)}, {operation}) ON CONFLICT DO NOTHING
            """, ct);

    private sealed record ProposalRow(Guid Id, long Ordinal, Guid AccountId, Guid OperationKey, Guid ActorId, int BillingRevision,
        string ScopeDigest, string Scope, DateTimeOffset RecordedAt, DateTimeOffset ExpiresAt);
}
