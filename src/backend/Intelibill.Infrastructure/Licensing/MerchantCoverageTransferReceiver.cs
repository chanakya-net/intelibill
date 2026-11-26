using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Intelibill.Shared.Contracts.V1.CoverageTransferApprovalsV1;

namespace Intelibill.Infrastructure.Licensing;

internal sealed class MerchantCoverageTransferReceiver(ApplicationDbContext db, EntitlementProjectionStore projection,
    IMembershipAuthorityRepository memberships, IAuthSessionRepository sessions, TimeProvider transportClock,
    [FromKeyedServices(ShopTrialsV1.DeliverySection)] TimeProvider clock) : ICoverageTransferReceiver
{
    public async Task<ErrorOr<Success>> ReceiveAsync(TransferCommandV1 c, byte[] body, VerifiedServiceAssertion proof,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"""
            SELECT set_config('lock_timeout',{ShopLicenceFence.LockTimeout},true),
                set_config('app.active_shop_id',{c.ShopId.ToString("D")},true),
                set_config('app.entitlement_account_id',{c.SourceAccountId.ToString("D")},true)
            """, ct);
        foreach (var id in new[] { c.SourceAccountId, c.DestinationAccountId }.Order())
            await db.Database.ExecuteSqlAsync($"SELECT public.take_merchant_licence_fence({id},NULL,false)", ct);
        await db.Database.ExecuteSqlAsync($"SELECT public.take_merchant_licence_fence(NULL,{c.ShopId},false)", ct);
        if (!await projection.ConsumeAsync(proof, ct)) return Error.Unauthorized();
        var prior = await PriorAsync(c.TransferId, c.Phase, ct);
        if (prior is null)
        {
            var reason = await CheckAsync(c, ct);
            await RecordAsync(c, body, proof.Digest, reason, ct);
        }
        else
        {
            if (prior.Digest != proof.Digest) return Error.Conflict("Coverage.TransferCommandMismatch");
            await db.Database.ExecuteSqlAsync($"""
                UPDATE public.entitlement_outbox SET status='Pending', attempts=0, last_failure=NULL, next_attempt_at={transportClock.GetUtcNow()}
                WHERE event_id={c.EventId} AND status='DeadLetter'
                """, ct);
        }
        if (transportClock.GetUtcNow() >= proof.ExpiresAt) return Error.Unauthorized();
        await tx.CommitAsync(ct);
        return Result.Success;
    }

    private async Task<string?> CheckAsync(TransferCommandV1 c, CancellationToken ct)
    {
        var prepared = await PriorAsync(c.TransferId, Prepare, ct);
        if (c.Phase != Prepare)
        {
            if (prepared?.Outcome != "Prepared" || c.PrepareDigest != prepared.Digest) return "PrepareChanged";
            var original = JsonSerializer.Deserialize<TransferCommandV1>(prepared.Command, EntitlementSnapshotsV1.Json);
            if (c with { Phase = Prepare, EventId = original!.EventId, PrepareDigest = null } != original)
                return "PrepareChanged";
            if (c.Phase == Abort) return await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT FROM public.shop_license_states WHERE shop_id={c.ShopId}
                    AND affiliation_request_id={c.TransferId}) AS "Value"
                """).SingleAsync(ct) ? "AlreadyActivated" : null;
            if (await PriorAsync(c.TransferId, Abort, ct) is not null) return "AlreadyAborted";
        }
        await sessions.LockUserAuthorityAsync(c.ShopOwner, ct);
        await memberships.LockShopAsync(c.ShopId, ct);
        await memberships.LockMembershipAsync(c.ShopId, c.ShopOwner, ct);
        var now = EntitlementSnapshotsV1.Instant(clock);
        if (c.Phase == Prepare && now >= c.CutoverAt || c.Phase == Consume && (now < c.CutoverAt ||
            c.Treatment == CoverageTransfersV1.ReviewedAllocation && now >= c.PaidThroughAt)) return "CutoverElapsed";
        var observed = await db.Database.SqlQuery<Inspection>($"""
            SELECT reason,permit_digest FROM public.coverage_transfer_shop_inspect({c.SourceAccountId},{c.ShopId},{c.AffiliationId},
                {c.TransferId},{c.ShopConsent},{c.ShopOwner},{c.MembershipId},{c.AuthorityRevision},{c.ProposalRevision},{c.TermsDigest},{c.PermitEvaluatedAt})
            """).SingleAsync(ct);
        if (observed.Reason is not null) return observed.Reason;
        if (observed.PermitDigest != c.PermitDigest) return "PermitEvidenceChanged";
        return await CheckCapacityAsync(c, ct);
    }

    private Task<string?> CheckCapacityAsync(TransferCommandV1 c, CancellationToken ct) =>
        db.Database.SqlQuery<string?>($"""
            SELECT CASE WHEN NOT EXISTS (SELECT FROM public.shop_license_states s WHERE s.shop_id={c.ShopId}
                AND s.account_id={c.SourceAccountId} AND s.affiliation_request_id={c.AffiliationId}
                AND s.term_id={c.TermId} AND s.member_allowance={c.MemberAllowance}
                AND s.state NOT IN ('Closed','Suspended','NeedsReview') AND s.suspension_mode IS NULL) THEN 'SourceChanged'
            WHEN EXISTS (SELECT FROM public.account_license_restrictions a WHERE a.account_id={c.SourceAccountId}
                AND a.restriction<>'Unrestricted') THEN 'AccountRestricted'
            WHEN (SELECT count(*) FROM public.shop_memberships WHERE shop_id={c.ShopId} AND status='Active')>{c.MemberAllowance}
                THEN 'MemberCapacityExceeded'
            WHEN EXISTS (SELECT FROM public.offline_license_permits WHERE shop_id={c.ShopId} AND valid_until>{c.CutoverAt})
                THEN 'PermitCrossesCutover'
            WHEN {c.Phase}='TransferPrepare' AND public.transfer_source_cutoff({c.ShopId},{c.AffiliationId}) IS NOT NULL
                THEN 'TransferBusy' END AS "Value"
            """).SingleAsync(ct);

    private Task<Prior?> PriorAsync(Guid transfer, string phase, CancellationToken ct) => db.Database.SqlQuery<Prior>($"""
        SELECT digest,outcome,command FROM public.merchant_transfer_decisions WHERE transfer_id={transfer} AND phase={phase}
        """).SingleOrDefaultAsync(ct);

    private async Task RecordAsync(TransferCommandV1 c, byte[] body, string digest, string? reason, CancellationToken ct)
    {
        var now = EntitlementSnapshotsV1.Instant(clock);
        var outcome = reason is not null ? "Refused" : c.Phase switch { Prepare => "Prepared", Consume => "Consumed", _ => "Aborted" };
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.merchant_transfer_decisions(event_id,transfer_id,shop_id,source_account_id,destination_account_id,
                affiliation_id,cutover_at,paid_through_at,phase,outcome,digest,command,recorded_at)
            VALUES({c.EventId},{c.TransferId},{c.ShopId},{c.SourceAccountId},{c.DestinationAccountId},{c.AffiliationId},{c.CutoverAt},
                {c.PaidThroughAt},{c.Phase},{outcome},{digest},{body},{now})
            """, ct);
        var ack = new TransferReceiptV1(1, c.EventId, c.Phase, c.TransferId, c.SourceAccountId, c.DestinationAccountId, c.ShopId,
            digest, outcome, reason, now);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(ack, EntitlementSnapshotsV1.Json);
        await new LeasedHttpOutbox(db).AppendAsync(c.EventId, c.SourceAccountId, c.ShopId, Receipt, bytes,
            SignedServiceAssertion.Digest(bytes), EntitlementSnapshotsV1.Instant(transportClock), ct);
    }

    private sealed record Prior(string Digest, string Outcome, byte[] Command);
    private sealed record Inspection(string? Reason, string PermitDigest);
}
