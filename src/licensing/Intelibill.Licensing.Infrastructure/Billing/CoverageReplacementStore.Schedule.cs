using ErrorOr;
using Intelibill.Licensing.Application.BillingAccounts;
using Intelibill.Licensing.Application.Changes;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Billing;

internal sealed partial class CoverageReplacementStore
{
    private static Error Changed => Error.Conflict("Coverage.ReplacementChanged");

    public async Task<ErrorOr<CoverageReplacementV1>> ScheduleAsync(Guid account, Guid term, Guid actor,
        ScheduleCoverageReplacementV1 command, CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(clock);
        var hash = CheckoutOrdersV1.Digest("coverage-replacement/1", term, command);
        var previous = await LookupAsync(account, actor, command.OperationKey, ct);
        if (previous is not null)
            return previous.Hash == hash ? await Rows(account, now).SingleAsync(r => r.ReplacementId == previous.Id, ct)
                : BillingAccountErrors.PayloadMismatch;
        var source = await terms.SourceAsync(account, term, now, ct);
        if (source is null || ScheduledChangeValidator.ValidateSource(source, now, command.ObservedCapacity,
                command.ObservedMemberAllowance, command.ObservedEffectiveAt) is not null) return Changed;
        var outgoing = source.Shops.SingleOrDefault(s => s.ShopId == command.OutgoingShopId);
        if (outgoing is null) return Changed;
        var id = Guid.NewGuid();
        var expires = ScheduledChangeValidator.RenewalWindowEnd(source);
        await RetireObsoleteAsync(account, term, command.IncomingShopId, now, ct);
        var count = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.coverage_replacements(replacement_id,account_id,term_id,outgoing_shop_id,outgoing_shop_name,
                outgoing_affiliation_id,incoming_shop_id,incoming_shop_name,incoming_affiliation_id,capacity,member_allowance,
                effective_at,permit_deadline,created_at,expires_at,requester_id,requester_revision,operation_key,request_hash,
                source_hash,state)
            SELECT {id},{account},{term},{outgoing.ShopId},{outgoing.Name},{outgoing.RequestId},a.shop_id,a.shop_name,a.request_id,
                {source.Capacity},{command.ObservedMemberAllowance},{source.EndsAt},{command.ObservedPermitDeadline}::timestamptz,
                {now},{expires},{actor},m.revision,{command.OperationKey},{hash},{source.Hash()},'AwaitingConsent'
            FROM public.shop_affiliations a JOIN public.billing_account_members m USING(account_id)
            WHERE a.account_id={account} AND a.shop_id={command.IncomingShopId}
                AND m.merchant_user_id={actor} AND m.status=1 AND m.role=1
                AND public.slot_shop_eligible({account},a.shop_id,{now},'infinity')
                AND public.replacement_term_available({account},{term})
                AND public.replacement_permit_end({account},{outgoing.ShopId},{outgoing.RequestId},{now})
                    IS NOT DISTINCT FROM {command.ObservedPermitDeadline}::timestamptz
            ON CONFLICT DO NOTHING
            """, ct);
        return count == 1 ? await Rows(account, now).SingleAsync(r => r.ReplacementId == id, ct) : Changed;
    }

    private async Task RetireObsoleteAsync(Guid account, Guid term, Guid incoming, DateTimeOffset now, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.replacement_evaluated_at',{now}::text,true)", ct);
        await db.Database.ExecuteSqlAsync($"""
            UPDATE public.coverage_replacements r SET state='Blocked',reason='SourceChanged'
            WHERE account_id={account} AND (incoming_shop_id={incoming} OR term_id={term}) AND state IN ('AwaitingConsent','Scheduled')
                AND public.replacement_stale(r.replacement_id,{now})
            """, ct);
    }

    public async Task<ErrorOr<CoverageReplacementV1>> RecoverAsync(Guid account, Guid term, Guid actor,
        Guid key, CancellationToken ct) => await LookupAsync(account, actor, key, ct) is { } prior &&
        await Rows(account, EntitlementSnapshotsV1.Instant(clock)).SingleOrDefaultAsync(r => r.ReplacementId == prior.Id && r.TermId == term, ct) is { } row
            ? row : Error.NotFound("Coverage.ReplacementNotFound");

    private Task<Operation?> LookupAsync(Guid account, Guid actor, Guid key, CancellationToken ct) =>
        db.Database.SqlQuery<Operation>($"""
            SELECT replacement_id AS id,request_hash AS hash FROM public.coverage_replacements
            WHERE account_id={account} AND requester_id={actor} AND operation_key={key}
            """).SingleOrDefaultAsync(ct);

    private IQueryable<CoverageReplacementV1> Rows(Guid account, DateTimeOffset now) =>
        db.Database.SqlQuery<CoverageReplacementV1>($"""
            SELECT replacement_id,account_id,term_id,outgoing_shop_id,outgoing_shop_name,incoming_shop_id,incoming_shop_name,
                incoming_affiliation_id,capacity,member_allowance,effective_at,permit_deadline,created_at,expires_at,
                CASE WHEN public.replacement_stale(replacement_id,{now})
                    THEN 'Blocked' ELSE state END AS state, reason,public.replacement_activation(replacement_id,{now}) AS activation
            FROM public.coverage_replacements WHERE account_id={account}
            """);

    private sealed record Operation(Guid Id, string Hash);

    public async Task<ErrorOr<CoverageReplacementV1>> OwnerAsync(Guid account, Guid shop, Guid replacement, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            SELECT set_config('app.affiliation_shop_id',{shop.ToString("D")},true),
                set_config('app.replacement_id',{replacement.ToString("D")},true)
            """, ct);
        return await Rows(account, EntitlementSnapshotsV1.Instant(clock))
            .SingleOrDefaultAsync(r => r.IncomingShopId == shop && r.ReplacementId == replacement, ct) is { } row
            ? row : Error.NotFound("Coverage.ReplacementNotFound");
    }
}
