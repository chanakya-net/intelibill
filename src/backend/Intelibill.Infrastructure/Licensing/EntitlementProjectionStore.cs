using ErrorOr;
using System.Text.Json;
using Intelibill.Application.Features.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Delivery;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Licensing;

public sealed class EntitlementProjectionStore(ApplicationDbContext db, TimeProvider clock, ShopLicenceFence fence)
{
    private static Error Replayed => Error.Unauthorized("Entitlement.Replayed");
    private static Error Conflict => Error.Conflict("Entitlement.EventMismatch");
    public async Task<ErrorOr<byte[]>> ApplyAsync(EntitlementSnapshotV1 snapshot, byte[] body,
        VerifiedServiceAssertion proof, CancellationToken ct)
    {
        var receipt = await ApplyWithChangeAsync(snapshot, body, proof, ct);
        return receipt.IsError ? receipt.Errors : receipt.Value.Acknowledgement;
    }

    public async Task<ErrorOr<EntitlementProjectionReceipt>> ApplyWithChangeAsync(EntitlementSnapshotV1 snapshot, byte[] body,
        VerifiedServiceAssertion proof, CancellationToken ct)
    {
        var received = EntitlementSnapshotsV1.Instant(clock);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var entered = await EnterAsync(snapshot.AccountId, snapshot.ShopId, ct);
        if (entered.IsError)
            return entered.Errors;
        if (!await ConsumeAsync(proof, ct))
            return Replayed;

        var result = await ApplyOnceAsync(snapshot, body, proof, received, ct);
        if (clock.GetUtcNow() >= proof.ExpiresAt)
            return Replayed;
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task<ErrorOr<EntitlementProjectionReceipt>> ApplyOnceAsync(EntitlementSnapshotV1 snapshot, byte[] body,
        VerifiedServiceAssertion proof, DateTimeOffset received, CancellationToken ct)
    {
        var prior = await db.Database.SqlQuery<InboxRow>($"""
            SELECT digest, acknowledgement FROM public.entitlement_snapshot_inbox
            WHERE event_id = {snapshot.EventId}
            """).SingleOrDefaultAsync(ct);
        if (prior is not null)
            return prior.Digest == proof.Digest ? new EntitlementProjectionReceipt(prior.Acknowledgement, false) : Conflict;

        var account = await db.Set<AccountLicenseRestriction>().SingleOrDefaultAsync(row => row.AccountId == snapshot.AccountId, ct);
        var shop = await db.Set<ShopLicenseState>().SingleOrDefaultAsync(row => row.ShopId == snapshot.ShopId, ct);
        var bound = await HasBindingAsync(snapshot, shop, ct);
        // Only a backward stamp needs the preceding head's immutable signing time.
        var regresses = snapshot.Account?.UpstreamVerifiedAt < account?.UpstreamVerifiedAt ||
            snapshot.Shop?.UpstreamVerifiedAt < shop?.UpstreamVerifiedAt;
        var priorProofs = bound && regresses ? await fence.ReadProofIssuanceAsync(snapshot.ShopId, ct) : null;
        var reason = bound ? EntitlementConsumer.ReviewReason(snapshot, account, shop, priorProofs, proof.IssuedAt) : "AffiliationUnresolved";
        var appliedAt = EntitlementSnapshotsV1.Instant(clock);
        var nextAccount = reason is null && (account is null || snapshot.Account!.Revision > account.Revision);
        var nextShop = reason is null && (shop is null || snapshot.Shop!.Revision > shop.Revision);
        var ack = new EntitlementAcknowledgementV1(1, snapshot.EventId, proof.Digest, snapshot.AccountId, snapshot.ShopId,
            snapshot.AffiliationRequestId, snapshot.Account?.Revision, snapshot.Shop?.Revision,
            nextAccount ? snapshot.Account!.Revision : bound ? account?.Revision : null,
            nextShop ? snapshot.Shop!.Revision : bound ? shop?.Revision : null,
            reason is null ? "Applied" : "NeedsReview", reason ?? (nextAccount || nextShop ? "Applied" : "Superseded"), appliedAt);
        var receipt = JsonSerializer.SerializeToUtf8Bytes(ack, EntitlementSnapshotsV1.Json);
        if (!await ReserveInboxAsync(snapshot, proof.Digest, receipt, ct))
            return Conflict;

        db.Add(Evidence(snapshot, body, proof, received, appliedAt));
        if (nextAccount) UpdateAccount(snapshot, account);
        if (nextShop) UpdateShop(snapshot, shop);
        await db.SaveChangesAsync(ct);
        await new LeasedHttpOutbox(db).AppendAsync(snapshot.EventId, snapshot.AccountId, snapshot.ShopId,
            "Acknowledgement", receipt, SignedServiceAssertion.Digest(receipt), appliedAt, ct);
        return new EntitlementProjectionReceipt(receipt, nextAccount || nextShop);
    }

    internal async Task<ErrorOr<Success>> EnterAsync(Guid account, Guid shop, CancellationToken ct)
    {
        // These delivery transactions also bound their later inbox, evidence and outbox writes.
        await db.Database.ExecuteSqlAsync($"SELECT set_config('lock_timeout', {ShopLicenceFence.LockTimeout}, true)", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.active_shop_id', {shop.ToString("D")}, true),
                set_config('app.entitlement_account_id', {account.ToString("D")}, true)
            """, ct);
        return await fence.EnterProjectionAsync(account, shop, ct);
    }

    private async Task<bool> ConsumeAsync(VerifiedServiceAssertion proof, CancellationToken ct)
    {
        if (clock.GetUtcNow() >= proof.ExpiresAt) return false;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.entitlement_replay_id', {proof.ReplayId.ToString("D")}, true)
            """, ct);
        var count = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.entitlement_request_uses (replay_id, expires_at) VALUES ({proof.ReplayId}, {proof.ExpiresAt})
            ON CONFLICT (replay_id) DO NOTHING
            """, ct);
        return count == 1 && clock.GetUtcNow() < proof.ExpiresAt;
    }

    private Task<bool> HasBindingAsync(EntitlementSnapshotV1 snapshot, ShopLicenseState? prior, CancellationToken ct)
    {
        if (prior is not null && (prior.AccountId != snapshot.AccountId || prior.AffiliationRequestId != snapshot.AffiliationRequestId))
            return Task.FromResult(false);
        return db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.shop_affiliation_consent_events e
                LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = e.id
                WHERE e.shop_id = {snapshot.ShopId} AND e.account_id = {snapshot.AccountId}
                    AND e.request_id = {snapshot.AffiliationRequestId} AND e.kind = 'Accepted'
                    AND r.status IS DISTINCT FROM 'NeedsReview') AS "Value"
            """).SingleAsync(ct);
    }

    private async Task<bool> ReserveInboxAsync(EntitlementSnapshotV1 snapshot, string digest, byte[] receipt, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.entitlement_snapshot_inbox (event_id, account_id, shop_id, digest, acknowledgement)
            VALUES ({snapshot.EventId}, {snapshot.AccountId}, {snapshot.ShopId}, {digest}, {receipt})
            ON CONFLICT (event_id) DO NOTHING
            """, ct) == 1;

    private void UpdateAccount(EntitlementSnapshotV1 snapshot, AccountLicenseRestriction? account)
    {
        var source = snapshot.Account!;
        if (account is null)
        {
            account = new() { AccountId = snapshot.AccountId };
            db.Add(account);
        }
        account.Apply(source.Revision, source.UpstreamVerifiedAt, source.Restriction, source.ReleasedRevision, snapshot.EventId,
            source.Suspension?.EffectiveAt, source.Suspension?.CustomerExplanation, source.PreviousReleaseRevision);
    }

    private void UpdateShop(EntitlementSnapshotV1 snapshot, ShopLicenseState? shop)
    {
        var source = snapshot.Shop!;
        if (shop is null)
        {
            shop = new() { ShopId = snapshot.ShopId, AccountId = snapshot.AccountId, AffiliationRequestId = snapshot.AffiliationRequestId };
            db.Add(shop);
        }
        shop.Apply(source.Revision, source.UpstreamVerifiedAt, source.State, source.TermId, source.Basis, source.StartsAt,
            source.AccessEndsAt, source.NextBoundary, source.ShopCapacity, source.MemberAllowance, snapshot.EventId,
            source.Suspension?.Mode, source.Suspension?.EffectiveAt, source.Suspension?.CustomerExplanation,
            source.ReleasedRevision, source.ReopenedRevision);
    }

    private static LicenseGrantEvidence Evidence(EntitlementSnapshotV1 snapshot, byte[] body, VerifiedServiceAssertion proof,
        DateTimeOffset received, DateTimeOffset applied) => new()
    {
        EventId = snapshot.EventId, AccountId = snapshot.AccountId, ShopId = snapshot.ShopId,
        AffiliationRequestId = snapshot.AffiliationRequestId, AccountRevision = snapshot.Account?.Revision,
        ShopRevision = snapshot.Shop?.Revision, AccountUpstreamVerifiedAt = ValidDate(snapshot.Account?.UpstreamVerifiedAt),
        ShopUpstreamVerifiedAt = ValidDate(snapshot.Shop?.UpstreamVerifiedAt), IssuedAt = proof.IssuedAt, ReceivedAt = received,
        AppliedAt = applied, KeyId = proof.KeyId, PublicKey = proof.PublicKey, Assertion = proof.Assertion, Digest = proof.Digest,
        Payload = body,
    };

    private static DateTimeOffset? ValidDate(DateTimeOffset? value) =>
        value is { } instant && EntitlementSnapshotsV1.IsInstant(instant) ? instant : null;

    private sealed record InboxRow(string Digest, byte[] Acknowledgement);
}

public sealed record EntitlementProjectionReceipt(byte[] Acknowledgement, bool Changed);
