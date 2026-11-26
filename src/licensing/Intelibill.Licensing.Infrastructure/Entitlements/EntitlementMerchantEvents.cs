using ErrorOr;
using Intelibill.Licensing.Infrastructure.Billing;
using Intelibill.Licensing.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Licensing.Infrastructure.Entitlements;

public sealed class EntitlementMerchantEvents
{
    private readonly LicensingDbContext db;
    private readonly TimeProvider clock;
    private readonly EntitlementPublisher publisher;
    private readonly CoverageSlotConsentAcknowledgements slots;
    private readonly ReplacementConsentAcknowledgements replacements;
    private readonly CoverageExceptionAcknowledgements coverageExceptions;
    private readonly CapacityReservationAcknowledgements increases;
    private readonly ReductionReservationAcknowledgements reductions;
    private readonly CoverageTransferAcknowledgements applications;
    private readonly CoverageTransferStore transfers;

    internal EntitlementMerchantEvents(LicensingDbContext db, TimeProvider clock, EntitlementPublisher publisher,
        CapacityReservationAcknowledgements increases, ReductionReservationAcknowledgements reductions, CoverageSlotConsentAcknowledgements slots,
        ReplacementConsentAcknowledgements replacements, CoverageExceptionAcknowledgements coverageExceptions,
        CoverageTransferStore transfers, CoverageTransferAcknowledgements applications)
    {
        this.applications = applications;
        this.transfers = transfers;
        this.slots = slots;
        this.replacements = replacements;
        this.coverageExceptions = coverageExceptions;
        this.increases = increases;
        this.reductions = reductions;
        this.db = db;
        this.clock = clock;
        this.publisher = publisher;
    }

    public Task<ErrorOr<Success>> TransferReceiptAsync(TransferReceiptV1 r, byte[] body, VerifiedServiceAssertion proof, CancellationToken ct) =>
        !r.IsValid()
            ? Task.FromResult<ErrorOr<Success>>(Error.Validation("Coverage.TransferReceiptInvalid"))
            : InEventAsync(r.SourceAccountId,r.EventId,CoverageTransferApprovalsV1.Receipt,body,proof,
                token => applications.ReceiveAsync(r,token),ct,r.DestinationAccountId);

    /// <summary>The named shop owner's transfer decision: evidence only, under the source account lock.</summary>
    public Task<ErrorOr<Success>> CoverageTransferConsentAsync(CoverageTransferShopConsentV1 c, byte[] body,
        VerifiedServiceAssertion proof, CancellationToken ct) =>
        !c.IsValid()
            ? Task.FromResult<ErrorOr<Success>>(Error.Validation("Coverage.TransferInvalid"))
            : InEventAsync(c.AccountId, c.EventId, CoverageTransfersV1.ConsentKind, body, proof,
                token => transfers.ApplyShopConsentAsync(c, token), ct);

    public Task<ErrorOr<Success>> CoverageExceptionConsentAsync(CoverageReplacementExceptionConsentV1 c, byte[] body,
        VerifiedServiceAssertion proof, CancellationToken ct) =>
        !c.IsValid()
            ? Task.FromResult<ErrorOr<Success>>(Error.Validation("Coverage.ExceptionInvalid"))
            : InEventAsync(c.AccountId, c.EventId, CoverageReplacementExceptionsV1.ConsentKind, body, proof,
                token => coverageExceptions.ApplyConsentAsync(c, token), ct);

    public Task<ErrorOr<Success>> CoverageExceptionReceiptAsync(CoverageExceptionReceiptV1 ack, byte[] body,
        VerifiedServiceAssertion proof, CancellationToken ct) =>
        !ack.IsValid()
            ? Task.FromResult<ErrorOr<Success>>(Error.Validation("Coverage.ExceptionInvalidReceipt"))
            : InEventAsync(ack.AccountId, ack.EventId, CoverageReplacementExceptionsV1.ReceiptKind, body, proof,
                token => coverageExceptions.ApplyReceiptAsync(ack, token), ct);

    public Task<ErrorOr<Success>> ReplacementConsentAsync(ReplacementConsentV1 c, byte[] body, VerifiedServiceAssertion proof, CancellationToken ct)
    {
        if (c.Kind is not ("Accepted" or "Revoked") || c.SchemaVersion != 1 || c.EventId == Guid.Empty || c.ReplacementId == Guid.Empty || c.AccountId == Guid.Empty ||
            c.TermId == Guid.Empty || c.ShopId == Guid.Empty || c.AffiliationRequestId == Guid.Empty || c.ActorId == Guid.Empty ||
            c.MembershipId == Guid.Empty || c.AuthorityVersion < 0 || c.ActiveMembers < 1 || c.MemberAllowance < 1 ||
            !EntitlementSnapshotsV1.IsDigest(c.SeatHash) || !EntitlementSnapshotsV1.IsInstant(c.RecordedAt))
            return Task.FromResult<ErrorOr<Success>>(Error.Validation("Coverage.ReplacementInvalid"));
        return InEventAsync(c.AccountId, c.EventId, CoverageReplacementsV1.Kind, body, proof, token => replacements.ApplyAsync(c, token), ct);
    }

    public Task<ErrorOr<Success>> SlotConsentAsync(CoverageSlotConsentV1 c, byte[] body, VerifiedServiceAssertion proof, CancellationToken ct)
    {
        if (c.SchemaVersion != 1 || c.EventId == Guid.Empty || c.AssignmentId == Guid.Empty || c.AccountId == Guid.Empty ||
            c.TermId == Guid.Empty || c.ShopId == Guid.Empty || c.AffiliationRequestId == Guid.Empty || c.ActorId == Guid.Empty ||
            c.MembershipId == Guid.Empty || c.AuthorityVersion < 0 || c.ActiveMembers < 1 || c.MemberAllowance < 1 ||
            !EntitlementSnapshotsV1.IsDigest(c.SeatHash) || !EntitlementSnapshotsV1.IsInstant(c.AcceptedAt))
            return Task.FromResult<ErrorOr<Success>>(Error.Validation("Coverage.SlotInvalid"));
        return InEventAsync(c.AccountId, c.EventId, CoverageSlotsV1.Kind, body, proof, token => slots.ApplyAsync(c, token), ct);
    }

    public Task<ErrorOr<Success>> ReductionAsync(ReductionReceiptV1 ack, byte[] body, VerifiedServiceAssertion proof,
        CancellationToken ct)
    {
        var valid = ack.Outcome switch
        {
            "Reserved" => ack.Phase == ReductionTransportV1.ReserveKind && ack.Reason is null,
            "Applied" => ack.Phase == ReductionTransportV1.ConsumeKind && ack.Reason is null,
            "NeedsResolution" => ack.Phase is ReductionTransportV1.ReserveKind or ReductionTransportV1.ConsumeKind &&
                ack.Reason is "MemberCapacityExceeded" or "ReservationExpired" or "ReservationChanged" or "ReservationBusy" or "SourceChanged",
            "NeedsReview" => ack.Phase is ReductionTransportV1.ReserveKind or ReductionTransportV1.ConsumeKind &&
                ack.Reason is "SourceChanged" or "ReservationChanged" or "ReservationExpired" or "ReservationBusy",
            _ => false,
        };
        if (!valid || ack.SchemaVersion != 1 || ack.EventId == Guid.Empty || ack.ScheduleId == Guid.Empty ||
            ack.AccountId == Guid.Empty || ack.TermId == Guid.Empty || ack.OrderId == Guid.Empty ||
            ack.Generation <= 0 || !EntitlementSnapshotsV1.IsDigest(ack.CommandDigest))
            return Task.FromResult<ErrorOr<Success>>(Error.Validation("Reduction.InvalidReceipt"));
        return InEventAsync(ack.AccountId, ack.EventId, ReductionTransportV1.ReceiptKind, body, proof,
            token => reductions.ApplyAsync(ack, token), ct);
    }

    public Task<ErrorOr<Success>> CapacityAsync(CapacityReceiptV1 ack, byte[] body, VerifiedServiceAssertion proof,
        CancellationToken ct)
    {
        var valid = ack.Outcome switch
        {
            "Reserved" => ack.Phase == CapacityReservationV1.Reserve && ack.Reason is null,
            "Applied" => ack.Phase == CapacityReservationV1.Consume && ack.Reason is null,
            "NeedsReview" => ack.Phase is CapacityReservationV1.Reserve or CapacityReservationV1.Consume &&
                ack.Reason is "ReservationExpired" or "ReservationChanged" or "SourceChanged" or "ReservationBusy",
            _ => false,
        };
        if (!valid || ack.SchemaVersion != 1 || ack.EventId == Guid.Empty || ack.AccountId == Guid.Empty ||
            ack.TermId == Guid.Empty || ack.OrderId == Guid.Empty || !EntitlementSnapshotsV1.IsDigest(ack.CommandDigest))
            return Task.FromResult<ErrorOr<Success>>(Error.Validation("Capacity.InvalidReceipt"));
        return InEventAsync(ack.AccountId, ack.EventId, CapacityReservationV1.Receipt, body, proof,
            token => increases.ApplyAsync(ack, token), ct);
    }

    public Task<ErrorOr<Success>> AcknowledgeAsync(EntitlementAcknowledgementV1 ack, byte[] body, VerifiedServiceAssertion proof,
        CancellationToken ct)
    {
        if (!ValidAck(ack))
            return Task.FromResult<ErrorOr<Success>>(Error.Validation("Entitlement.InvalidMerchantEvent"));
        return InEventAsync(ack.AccountId, ack.EventId, "Acknowledgement", body, proof,
            token => SaveAcknowledgementAsync(ack, token), ct);
    }

    public Task<ErrorOr<Success>> ReconcileAsync(EntitlementReconciliationV1 request, byte[] body, VerifiedServiceAssertion proof,
        CancellationToken ct)
    {
        if (request.SchemaVersion != 1 || request.EventId == Guid.Empty || request.AccountId == Guid.Empty ||
            request.ShopId == Guid.Empty || request.AffiliationRequestId == Guid.Empty ||
            request.AccountRevision is <= 0 || request.ShopRevision is <= 0)
            return Task.FromResult<ErrorOr<Success>>(Error.Validation("Entitlement.InvalidMerchantEvent"));
        return InEventAsync(request.AccountId, request.EventId, "Reconciliation", body, proof,
            token => SaveReconciliationAsync(request, token), ct);
    }

    /// <summary>
    /// Retains one device observation. Its account, shop and affiliation must match the binding at observation time;
    /// a permit is kept once, so a second event naming the same permit conflicts and stores nothing.
    /// </summary>
    public Task<ErrorOr<Success>> ObserveDeviceAsync(OfflineDeviceObservationV1 seen, byte[] body, VerifiedServiceAssertion proof,
        CancellationToken ct)
    {
        if (!seen.IsValid())
            return Task.FromResult<ErrorOr<Success>>(Error.Validation("Entitlement.InvalidMerchantEvent"));
        return InEventAsync(seen.AccountId, seen.EventId, OfflineDeviceObservationsV1.Kind, body, proof,
            token => SaveObservationAsync(seen, token), ct);
    }

    private async Task<bool> SaveObservationAsync(OfflineDeviceObservationV1 seen, CancellationToken ct)
    {
        var permit = seen.Permit;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.offline_device_observations (event_id, account_id, shop_id, affiliation_request_id, device_ref,
                observation, observed_at, permit_id, not_before, valid_until, permit_limit, account_revision, shop_revision,
                authority_verified_at)
            SELECT {seen.EventId}, a.account_id, a.shop_id, a.request_id, {seen.DeviceRef}, {seen.Observation}, {seen.ObservedAt},
                {permit?.PermitId}::uuid, {permit?.NotBefore}::timestamptz, {permit?.ValidUntil}::timestamptz, {permit?.Limit}::text,
                {permit?.AccountRevision}::bigint, {permit?.ShopRevision}::bigint, {permit?.AuthorityVerifiedAt}::timestamptz
            FROM (
                SELECT account_id, shop_id, request_id FROM public.current_shop_affiliations
                UNION ALL
                SELECT source_account_id, shop_id, predecessor_id FROM public.coverage_transfer_bindings
                WHERE cutover_at > {seen.ObservedAt}
            ) a
            WHERE a.account_id = {seen.AccountId} AND a.shop_id = {seen.ShopId} AND a.request_id = {seen.AffiliationRequestId}
            ON CONFLICT (permit_id) DO NOTHING
            """, ct) == 1;
    }

    private async Task<ErrorOr<Success>> InEventAsync(Guid account, Guid id, string kind, byte[] body, VerifiedServiceAssertion proof,
        Func<CancellationToken, Task<bool>> apply, CancellationToken ct, Guid? counterparty = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (counterparty is { } other) await CoverageTransferAcknowledgements.EnterPairAsync(db,account,other,ct);
        else await EnterAccountAsync(db, account, ct);
        if (!await ConsumeAsync(proof, ct))
            return Error.Unauthorized("Entitlement.Replayed");

        var prior = await db.Database.SqlQuery<string>($"""
            SELECT digest AS "Value" FROM public.entitlement_merchant_inbox WHERE event_id = {id} AND kind = {kind}
            """).SingleOrDefaultAsync(ct);
        var matched = prior is not null
            ? prior == proof.Digest
            : await SaveInboxAsync(id, kind, account, body, proof, ct) && await apply(ct);
        if (clock.GetUtcNow() >= proof.ExpiresAt)
            return Error.Unauthorized("Entitlement.Replayed");
        if (matched || prior is not null)
            await tx.CommitAsync(ct);
        return matched ? Result.Success : Error.Conflict("Entitlement.EventMismatch");
    }

    private async Task<bool> SaveAcknowledgementAsync(EntitlementAcknowledgementV1 ack, CancellationToken ct)
    {
        var matches = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.entitlement_snapshots s JOIN public.entitlement_messages m
                USING (event_id, account_id, shop_id) WHERE s.event_id = {ack.EventId} AND s.account_id = {ack.AccountId}
                AND s.shop_id = {ack.ShopId} AND s.affiliation_request_id = {ack.AffiliationRequestId}
                AND s.account_revision = {ack.AccountRevision} AND s.shop_revision = {ack.ShopRevision}
                AND m.digest = {ack.Digest} AND ({ack.ActivatedReplacementId}::uuid IS NULL OR (
                    {ack.Outcome}='Applied' AND {ack.Disposition}='Applied' AND {ack.AppliedShopRevision}=s.shop_revision
                    AND convert_from(m.payload,'UTF8')::jsonb->'shop'->>'replacementId'={ack.ActivatedReplacementId}::text))
                AND ({ack.ActivatedExceptionId}::uuid IS NULL OR (
                    {ack.Outcome}='Applied' AND {ack.Disposition}='Applied' AND {ack.AppliedShopRevision}=s.shop_revision
                    AND convert_from(m.payload,'UTF8')::jsonb->'shop'->>'replacementExceptionId'={ack.ActivatedExceptionId}::text
                    AND convert_from(m.payload,'UTF8')::jsonb->'shop'->>'replacementExceptionLeg'={ack.ActivatedExceptionLeg}::text
                    AND (convert_from(m.payload,'UTF8')::jsonb->'shop'->>'replacementExceptionGeneration')::bigint={ack.ActivatedExceptionGeneration}::bigint))) AS "Value"
            """).SingleAsync(ct);
        if (!matches) return false;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public.entitlement_outbox SET status = {ack.Outcome},
                lease_token = NULL, lease_expires_at = NULL, last_failure = NULL
            WHERE event_id = {ack.EventId} AND account_id = {ack.AccountId}
            """, ct) == 1;
    }

    private async Task<bool> SaveReconciliationAsync(EntitlementReconciliationV1 request, CancellationToken ct)
    {
        var matches = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.current_shop_affiliations WHERE account_id = {request.AccountId}
                AND shop_id = {request.ShopId} AND request_id = {request.AffiliationRequestId}) AS "Value"
            """).SingleAsync(ct);
        return matches && await publisher.PublishAsync(request.AccountId, request.ShopId, reconcile: true, ct);
    }

    internal static async Task EnterAccountAsync(LicensingDbContext db, Guid account, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.billing_account_id', {account.ToString("D")}, true)
            """, ct);
        await BillingAccountLock.TakeAsync(db, account, ct);
    }

    private async Task<bool> ConsumeAsync(VerifiedServiceAssertion proof, CancellationToken ct)
    {
        if (clock.GetUtcNow() >= proof.ExpiresAt) return false;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.entitlement_replay_id', {proof.ReplayId.ToString("D")}, true)
            """, ct);
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.entitlement_request_uses (replay_id, expires_at) VALUES ({proof.ReplayId}, {proof.ExpiresAt})
            ON CONFLICT (replay_id) DO NOTHING
            """, ct);
        return inserted == 1 && clock.GetUtcNow() < proof.ExpiresAt;
    }

    private async Task<bool> SaveInboxAsync(Guid id, string kind, Guid account, byte[] body, VerifiedServiceAssertion proof,
        CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.entitlement_merchant_inbox (event_id, kind, account_id, digest, payload, assertion, public_key)
            VALUES ({id}, {kind}, {account}, {proof.Digest}, {body}, {proof.Assertion}, {proof.PublicKey})
            ON CONFLICT (event_id, kind) DO NOTHING
            """, ct) == 1;

    private bool ValidAck(EntitlementAcknowledgementV1 ack) =>
        ack.SchemaVersion == 1 && ack.EventId != Guid.Empty && ack.AccountId != Guid.Empty && ack.ShopId != Guid.Empty &&
        ack.AffiliationRequestId != Guid.Empty && EntitlementSnapshotsV1.IsDigest(ack.Digest) &&
        ack.AccountRevision > 0 && ack.ShopRevision > 0 && ack.AppliedAccountRevision is not <= 0 &&
        ack.AppliedShopRevision is not <= 0 && EntitlementSnapshotsV1.IsInstant(ack.AppliedAt) && ack.AppliedAt <= clock.GetUtcNow() &&
        ValidExceptionAck(ack) && IsValidDisposition(ack);

    private static bool ValidExceptionAck(EntitlementAcknowledgementV1 ack) =>
        ack.ActivatedExceptionId is null
            ? ack.ActivatedExceptionLeg is null && ack.ActivatedExceptionGeneration is null
            : ack.ActivatedExceptionId != Guid.Empty && CoverageReplacementExceptionsV1.IsSnapshotLeg(ack.ActivatedExceptionLeg)
                && ack.ActivatedExceptionGeneration is > 0;

    private static bool IsValidDisposition(EntitlementAcknowledgementV1 ack) => ack.Outcome switch
    {
        "Applied" => ack.Disposition is "Applied" or "Superseded" &&
            ack.AppliedAccountRevision >= ack.AccountRevision && ack.AppliedShopRevision >= ack.ShopRevision,
        "NeedsReview" => ack.Disposition is "AffiliationUnresolved" or "MissingScope" or "InvalidAccountScope" or
            "InvalidShopScope" or "ConflictingAccountScope" or "ConflictingShopScope",
        _ => false,
    };
}
