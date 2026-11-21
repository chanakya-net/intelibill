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

    public async Task<SavedClosureProposal?> ProposalAsync(Guid account, Guid? id, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.Database.SqlQuery<ProposalRow>($"""
            SELECT id, account_id, actor_id, billing_revision, scope_digest, scope::text, recorded_at, expires_at
            FROM public.operational_closure_proposals WHERE account_id = {account}
                AND (({id}::uuid IS NULL AND expires_at > {now}) OR id = {id})
            ORDER BY ordinal DESC LIMIT 1
            """).SingleOrDefaultAsync(ct);
        return row is null ? null : new(row.Id, row.AccountId, row.ActorId, row.BillingRevision, row.ScopeDigest,
            JsonSerializer.Deserialize<ClosureTargetV1[]>(row.Scope, EntitlementSnapshotsV1.Json)!, row.RecordedAt, row.ExpiresAt);
    }

    public Task InsertProposalAsync(SavedClosureProposal item, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO public.operational_closure_proposals
            (id, account_id, actor_id, billing_revision, financial_treatment, scope_digest, scope, recorded_at, expires_at)
        VALUES ({item.Id}, {item.AccountId}, {item.ActorId}, {item.BillingRevision}, {ShopClosuresV1.FinancialTreatment},
            {item.ScopeDigest}, {JsonSerializer.Serialize(item.Targets, EntitlementSnapshotsV1.Json)}::jsonb,
            {item.RecordedAt}, {item.ExpiresAt})
        """, ct);

    public Task<Guid[]> ReadNoticesAsync(Guid account, Guid proposal, CancellationToken ct) => db.Database.SqlQuery<Guid>($"""
        SELECT shop_id AS "Value" FROM public.operational_closure_consent_notices
        WHERE account_id = {account} AND proposal_id = {proposal}
        """).ToArrayAsync(ct);

    public Task RecordNoticeAsync(Guid account, Guid proposal, Guid shop, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.operational_closure_consent_notices (proposal_id, account_id, shop_id)
            VALUES ({proposal}, {account}, {shop}) ON CONFLICT DO NOTHING
            """, ct);

    private sealed record ProposalRow(Guid Id, Guid AccountId, Guid ActorId, int BillingRevision,
        string ScopeDigest, string Scope, DateTimeOffset RecordedAt, DateTimeOffset ExpiresAt);
}
