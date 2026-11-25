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
            SELECT public.slot_shop_eligible({account},{command.ShopId},{now},{source.EndsAt}) AS "Value"
            """).SingleAsync(ct);
        if (!eligible) return Error.Conflict("Coverage.SlotShopIneligible");
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_slot_assignments SET state='Expired'
            WHERE account_id={account} AND shop_id={command.ShopId} AND state='AwaitingConsent' AND expires_at<={now}
            """, ct);
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_slot_assignments(assignment_id,account_id,term_id,order_id,shop_id,shop_name,
                affiliation_request_id,requester_id,requester_revision,operation_key,capacity,member_allowance,
                coverage_ends_at,created_at,expires_at,state)
            SELECT {id},a.account_id,{term},{source.OrderId},a.shop_id,a.shop_name,a.request_id,{actor},m.revision,
                {command.OperationKey},{source.Capacity},{source.Members},{source.EndsAt},{now},least({source.EndsAt},{now.AddDays(7)}),'AwaitingConsent'
            FROM public.shop_affiliations a JOIN public.billing_account_members m USING(account_id)
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
            SELECT shop_id,shop_name FROM public.shop_affiliations a WHERE account_id={account}
                AND public.slot_shop_eligible({account},shop_id,{now},{source.EndsAt})
                AND NOT EXISTS(SELECT FROM public.coverage_slot_assignments r WHERE r.shop_id=a.shop_id
                    AND r.state='AwaitingConsent' AND r.expires_at>{now}) ORDER BY shop_name,shop_id LIMIT {size + 1} OFFSET {offset}
            """).ToArrayAsync(ct);
        var assignments = await Rows(account, now).Where(r => r.TermId == term).OrderByDescending(r => r.CreatedAt)
            .ThenBy(r => r.AssignmentId).Skip(offset).Take(size + 1).ToArrayAsync(ct);
        return new CoverageSlotTermV1(term, source.Capacity, source.Available ? source.Capacity-source.Occupied : 0,
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
                CASE WHEN a.state='AwaitingConsent' AND a.expires_at<={now} THEN 'Expired' ELSE a.state END AS state,
                CASE WHEN a.state<>'Applied' THEN 'NotApplicable' WHEN a.coverage_ends_at<={now} THEN 'TermEnded'
                    WHEN EXISTS(SELECT FROM public.applied_entitlement_shop_receipts r
                        JOIN public.entitlement_snapshots s USING(account_id,shop_id,shop_revision)
                        JOIN public.entitlement_messages m USING(event_id,account_id,shop_id)
                        WHERE r.account_id=a.account_id AND r.shop_id=a.shop_id AND r.affiliation_request_id=a.affiliation_request_id
                            AND r.shop_revision>=initial.shop_revision
                            AND convert_from(m.payload,'UTF8')::jsonb->'shop'->>'termId'=a.term_id::text)
                        THEN 'Acknowledged'
                    WHEN o.status IN ('NeedsReview','DeadLetter','Applied') THEN 'NeedsReview' ELSE 'Applying' END AS activation
            FROM public.coverage_slot_assignments a LEFT JOIN public.coverage_slot_assignment_deliveries d USING(assignment_id)
            LEFT JOIN public.entitlement_snapshots initial ON initial.event_id=d.event_id
            LEFT JOIN public.entitlement_outbox o ON o.event_id=d.event_id WHERE a.account_id={account}
            """);

    internal Task<Source?> SourceAsync(Guid account, Guid term, DateTimeOffset now, CancellationToken ct) =>
        db.Database.SqlQuery<Source>($"""
            SELECT f.order_id,t.ends_at,coalesce(i.capacity,t.capacity) AS capacity,coalesce(i.members,s.member_allowance) AS members,
                (SELECT count(*)::int FROM (SELECT shop_id FROM public.payment_fulfilment_shops WHERE term_id=t.term_id
                    UNION SELECT shop_id FROM public.shop_affiliations WHERE term_id=t.term_id) shops) AS occupied,
                (t.starts_at<={now} AND {now}<t.ends_at AND public.capacity_slot_available(t.account_id,t.term_id,t.capacity)) AS available
            FROM public.subscription_coverage_terms t JOIN public.payment_fulfilments f USING(account_id,term_id)
            JOIN public.subscription_term_facts s USING(account_id,term_id)
            LEFT JOIN LATERAL(SELECT capacity,members FROM public.capacity_increases
                WHERE account_id=t.account_id AND term_id=t.term_id AND state='Applied' ORDER BY target_revision DESC LIMIT 1) i ON true
            WHERE t.account_id={account} AND t.term_id={term} AND s.basis='Paid'
            """).SingleOrDefaultAsync(ct);

    internal sealed record Source(Guid OrderId, DateTimeOffset EndsAt, int Capacity, int Members, int Occupied, bool Available);
}
