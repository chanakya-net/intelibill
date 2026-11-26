using ErrorOr;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Coverage;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Licensing.Infrastructure.Restrictions;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed class CoverageSlotAssignmentStore(LicensingDbContext db,
    [FromKeyedServices(CheckoutOrdersV1.Policy)] TimeProvider clock) : ICoverageSlotAssignmentStore
{
    private DateTimeOffset Now => BillingAccountRules.StorageInstant(clock.GetUtcNow());
    private static Error Missing => Error.NotFound("Coverage.SlotNotFound");
    private static Error Busy => Error.Conflict("Coverage.SlotShopBusy");

    public async Task<ErrorOr<CoverageSlotAssignmentV1>> RequestAsync(Guid account, Guid term, Guid actor,
        AssignUnusedCoverageSlotV1 command, CancellationToken ct)
    {
        var prior = await ByKeyAsync(account, actor, command.OperationKey, ct);
        if (prior is not null)
            return prior.TermId == term && prior.ShopId == command.ShopId && prior.Capacity == command.ObservedCapacity
                && prior.CoverageEndsAt == command.ObservedPaidThrough ? prior : BillingAccountErrors.PayloadMismatch;
        var now = Now;
        var source = await SourceAsync(account, term, now, ct);
        if (source is null) return Error.Conflict("Coverage.SlotTermUnsupported");
        if (source.Capacity != command.ObservedCapacity || source.EndsAt != command.ObservedPaidThrough)
            return Error.Conflict("Coverage.SlotSourceChanged");
        if (!source.Available || await ClosureStore.AccountClosedAsync(db, account, ct)) return Error.Conflict("Coverage.SlotUnavailable");
        await db.Database.ExecuteSqlAsync($"""
            SELECT pg_advisory_xact_lock(hashtextextended('g'||{command.ShopId}::text,0))
            """, ct);
        var eligible = await db.Database.SqlQuery<bool>($"""
            SELECT public.slot_shop_eligible({account},{command.ShopId},{now},{source.AccessEndsAt}) AS "Value"
            """).SingleAsync(ct);
        if (!eligible) return Error.Conflict("Coverage.SlotShopIneligible");
        var id = Guid.NewGuid();
        await ExpireStalePendingAsync(account, command.ShopId, now, ct);
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_slot_assignments(assignment_id,account_id,term_id,order_id,shop_id,shop_name,
                affiliation_request_id,requester_id,requester_revision,operation_key,capacity,member_allowance,
                cancellation_revision,coverage_ends_at,created_at,expires_at,state)
            SELECT {id},a.account_id,{term},{source.OrderId},a.shop_id,a.shop_name,a.request_id,{actor},m.revision,
                {command.OperationKey},{source.Capacity},{source.Members},{source.CancellationRevision},
                {source.EndsAt},{now},least({source.EndsAt},{now.AddDays(7)}),'AwaitingConsent'
            FROM public.current_shop_affiliations a JOIN public.billing_account_members m USING(account_id)
            WHERE a.account_id={account} AND a.shop_id={command.ShopId} AND m.merchant_user_id={actor} AND m.status=1
            ON CONFLICT DO NOTHING
            """, ct);
        return inserted == 1 ? await Rows(account, now).SingleAsync(r => r.AssignmentId == id, ct) : Busy;
    }

    public async Task<ErrorOr<CoverageSlotTermV1>> ReadAsync(Guid account, Guid term, int page, CancellationToken ct)
    {
        const int size = CoverageSlotsV1.PageSize;
        if (page is < 1 or > int.MaxValue / size) return Error.Validation("Coverage.SlotInvalid");
        var offset = (page - 1) * size;
        var now = Now;
        var source = await SourceAsync(account, term, now, ct);
        if (source is null) return Missing;
        var shops = await db.Database.SqlQuery<SlotShopV1>($"""
            SELECT shop_id,shop_name FROM public.current_shop_affiliations a WHERE account_id={account}
                AND public.slot_shop_eligible({account},shop_id,{now},{source.AccessEndsAt})
                AND NOT EXISTS(SELECT FROM public.coverage_slot_assignments r
                    CROSS JOIN LATERAL public.slot_assignment_status(r.account_id,r.assignment_id,{now}) st
                    WHERE r.shop_id=a.shop_id AND st.projected_state='AwaitingConsent')
            ORDER BY shop_name,shop_id LIMIT {size + 1} OFFSET {offset}
            """).ToArrayAsync(ct);
        var assignments = await Rows(account, now).Where(r => r.TermId == term).OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.AssignmentId).Skip(offset).Take(size + 1).ToArrayAsync(ct);
        return new CoverageSlotTermV1(term, source.Capacity, source.Available ? source.Capacity - source.Occupied : 0,
            source.EndsAt, [.. shops.Take(size)], [.. assignments.Take(size)], shops.Length > size || assignments.Length > size);
    }

    public async Task<ErrorOr<CoverageSlotAssignmentV1>> RecoverAsync(Guid account, Guid term, Guid actor, Guid key, CancellationToken ct) =>
        await ByKeyAsync(account, actor, key, ct) is { } row && row.TermId == term ? row : Missing;

    public async Task<ErrorOr<CoverageSlotAssignmentV1>> OwnerAsync(Guid account, Guid shop, Guid assignment, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            SELECT set_config('app.affiliation_shop_id',{shop.ToString("D")},true),
                set_config('app.slot_assignment_id',{assignment.ToString("D")},true)
            """, ct);
        return await Rows(account, Now).SingleOrDefaultAsync(r => r.ShopId == shop && r.AssignmentId == assignment, ct)
            is { } row ? row : Missing;
    }

    private Task<int> ExpireStalePendingAsync(Guid account, Guid shop, DateTimeOffset now, CancellationToken ct) =>
        db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_slot_assignments a SET state='Expired'
            WHERE a.account_id={account} AND a.shop_id={shop} AND a.state='AwaitingConsent'
              AND EXISTS (SELECT FROM public.slot_assignment_status(a.account_id,a.assignment_id,{now}) st
                  WHERE st.projected_state IN ('Expired','Stale'))
            """, ct);

    private async Task<CoverageSlotAssignmentV1?> ByKeyAsync(Guid account, Guid actor, Guid key, CancellationToken ct)
    {
        var id = await db.Database.SqlQuery<Guid>($"""
            SELECT assignment_id AS "Value" FROM public.coverage_slot_assignments
            WHERE account_id={account} AND requester_id={actor} AND operation_key={key}
            """).SingleOrDefaultAsync(ct);
        return id == Guid.Empty ? null : await Rows(account, Now).SingleAsync(r => r.AssignmentId == id, ct);
    }

    private IQueryable<CoverageSlotAssignmentV1> Rows(Guid account, DateTimeOffset now) =>
        db.Database.SqlQuery<CoverageSlotAssignmentV1>($"""
            SELECT a.assignment_id,a.account_id,a.term_id,a.shop_id,a.affiliation_request_id,a.shop_name,a.capacity,
                a.member_allowance,a.coverage_ends_at,a.created_at,a.expires_at,a.applied_at,a.reason,
                st.projected_state AS state,st.activation AS activation,st.merchant_receipt AS merchant_receipt
            FROM public.coverage_slot_assignments a
            CROSS JOIN LATERAL public.slot_assignment_status(a.account_id,a.assignment_id,{now}) st
            WHERE a.account_id={account}
            """);

    internal Task<Source?> SourceAsync(Guid account, Guid term, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<Source>($"""
            SELECT f.order_id,ep.paid_through AS ends_at,
                public.term_effective_access_end(t.account_id,t.term_id) AS access_ends_at,coalesce(i.capacity,t.capacity) AS capacity,
                coalesce(i.members,s.member_allowance) AS members,
                coalesce(cancel.rev,0) AS cancellation_revision,
                (SELECT count(*)::int FROM public.term_effective_roster(t.account_id, t.term_id, NULL)) AS occupied,
                (t.starts_at<={now} AND {now}<ep.paid_through AND coalesce(cancel.rev,0)%2=0
                    AND public.capacity_slot_available(t.account_id,t.term_id,t.capacity)) AS available
            FROM public.subscription_coverage_terms t JOIN public.payment_fulfilments f USING(account_id,term_id)
            JOIN public.subscription_term_facts s USING(account_id,term_id)
            CROSS JOIN LATERAL (SELECT public.term_effective_paid_through(t.account_id,t.term_id) AS paid_through) ep
            LEFT JOIN LATERAL (SELECT max(c.revision) AS rev FROM public.scheduled_subscription_changes c
                WHERE c.account_id=t.account_id AND c.term_id=t.term_id) cancel ON true
            LEFT JOIN LATERAL (SELECT capacity,members FROM public.capacity_increases
                WHERE account_id=t.account_id AND term_id=t.term_id AND state='Applied' ORDER BY target_revision DESC LIMIT 1) i ON true
            WHERE t.account_id={account} AND t.term_id={term} AND s.basis='Paid'
            """).SingleOrDefaultAsync(ct);

    /// <summary>EndsAt is the shared paid-through; AccessEndsAt also carries any grace the assigned shop would hold.</summary>
    internal sealed record Source(Guid OrderId, DateTimeOffset EndsAt, DateTimeOffset AccessEndsAt, int Capacity, int Members,
        long CancellationRevision, int Occupied, bool Available);
}
