using ErrorOr;
using Intelibill.Application.Common.Licensing.Offline;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using PermitErrors = Intelibill.Application.Common.Errors.Errors.OfflinePermit;

namespace Intelibill.Infrastructure.Licensing;

/// <summary>
/// Tenant-scoped reads and inserts for offline licence evidence. Every query names the shop as
/// well as relying on row security, and nothing here updates or deletes: the tables refuse both.
/// </summary>
internal sealed class OfflineLicenceEvidenceStore(ApplicationDbContext db) : IOfflineLicenceEvidenceStore
{
    public async Task<bool> HasConflictingReceiptAsync(Guid shopId, string deviceId, string clientSaleId, string invoiceNumber,
        string payloadHash, CancellationToken ct) =>
        await FindConflictingReceiptIdAsync(shopId, deviceId, clientSaleId, invoiceNumber, payloadHash, ct) is not null;

    public Task<Guid?> FindConflictingReceiptIdAsync(Guid shopId, string deviceId, string clientSaleId, string invoiceNumber,
        string payloadHash, CancellationToken ct) =>
        db.OfflineSubmissions.AsNoTracking()
            .Where(s => s.ShopId == shopId && s.DeviceId == deviceId && s.PayloadHash != payloadHash &&
                (s.ClientSaleId == clientSaleId || s.InvoiceNumber == invoiceNumber))
            .OrderBy(s => s.ReceivedAt)
            .ThenBy(s => s.Id)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);

    public Task<bool> HasGrantForLeaseAsync(Guid shopId, Guid leaseId, CancellationToken ct) =>
        db.Set<OfflineLicenseGrant>().AnyAsync(g => g.ShopId == shopId && g.LeaseId == leaseId, ct);

    public bool IsBeingReserved(InvoiceLease lease) =>
        db.Database.CurrentTransaction is not null && db.Entry(lease).State == EntityState.Added;

    public async Task<OfflineAuthFacts?> ReadAuthAsync(Guid shopId, Guid userId, Guid sessionId, DateTimeOffset now,
        CancellationToken ct)
    {
        var live = await (from session in db.AuthSessions.AsNoTracking()
            join membership in db.ShopMemberships.AsNoTracking() on session.UserId equals membership.UserId
            where session.Id == sessionId && session.UserId == userId && session.RevokedAt == null && session.ExpiresAt > now
                && session.ActiveShopId == shopId && membership.ShopId == shopId
                && membership.Status == MembershipStatus.Active
                && (membership.Role == ShopRole.Owner || membership.Role == ShopRole.Manager)
            select new { membership.AuthorityVersion, session.ScopeVersion, session.ExpiresAt, session.SecurityVersion })
            .SingleOrDefaultAsync(ct);
        if (live is null)
            return null;

        // Offline, the device cannot renew past its current refresh credential or its session end.
        // Without a usable matching refresh credential the device has no valid offline auth lifetime.
        var refresh = await db.RefreshTokens.AsNoTracking()
            .Where(token => token.AuthSessionId == sessionId && token.UserId == userId
                && token.SecurityVersion == live.SecurityVersion && !token.IsRevoked
                && token.ConsumedAt == null && token.ExpiresAt > now)
            .MaxAsync(token => (DateTimeOffset?)token.ExpiresAt, ct);
        if (refresh is not { } usable)
            return null;

        var deadline = usable < live.ExpiresAt ? usable : live.ExpiresAt;
        return new OfflineAuthFacts(live.AuthorityVersion, live.ScopeVersion, OfflinePermitContractV1.Instant(deadline));
    }

    public async Task<ErrorOr<T>> ExecuteInSavepointAsync<T>(string name, Func<Task<ErrorOr<T>>> action, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A savepoint requires an open transaction.");
        await transaction.CreateSavepointAsync(name, ct);
        try
        {
            var result = await action();
            if (result.IsError)
                await transaction.RollbackToSavepointAsync(name, CancellationToken.None);
            await transaction.ReleaseSavepointAsync(name, CancellationToken.None);
            return result;
        }
        catch
        {
            await transaction.RollbackToSavepointAsync(name, CancellationToken.None);
            await transaction.ReleaseSavepointAsync(name, CancellationToken.None);
            throw;
        }
    }

    public void StageGrant(OfflineLicenseGrant grant) => db.Add(grant);

    public Task<OfflineLicenseGrant?> FindGrantAsync(Guid shopId, Guid grantId, CancellationToken ct) =>
        db.Set<OfflineLicenseGrant>().AsNoTracking().SingleOrDefaultAsync(row => row.ShopId == shopId && row.Id == grantId, ct);

    public Task<InvoiceLease?> FindLeaseAsync(Guid shopId, Guid leaseId, CancellationToken ct) =>
        db.Set<InvoiceLease>().AsNoTracking().SingleOrDefaultAsync(row => row.ShopId == shopId && row.Id == leaseId, ct);

    public async Task<OfflineSnapshotFacts> ReadSnapshotAsync(Guid shopId, Guid snapshotId, CancellationToken ct)
    {
        var facts = await db.Set<OfflineExpiryEvidence>().AsNoTracking()
            .Where(row => row.ShopId == shopId && row.SnapshotId == snapshotId)
            .GroupBy(_ => 1)
            .Select(rows => new
            {
                Count = rows.Count(), First = rows.Min(row => row.CapturedAt), Last = rows.Max(row => row.CapturedAt),
                Expiry = rows.Min(row => row.ExpiresAt), Oldest = rows.Min(row => row.CatalogVersion),
                Newest = rows.Max(row => row.CatalogVersion),
            }).SingleOrDefaultAsync(ct);
        return facts is null
            ? new OfflineSnapshotFacts(0, null, 0, null, null, null)
            : new OfflineSnapshotFacts(facts.Count, facts.Oldest, facts.Oldest == facts.Newest ? 1 : 2, facts.First, facts.Last,
                facts.Expiry);
    }

    public Task<OfflineLicensePermit?> FindPermitAsync(Guid shopId, Guid snapshotId, CancellationToken ct) =>
        db.Set<OfflineLicensePermit>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.ShopId == shopId && row.SnapshotId == snapshotId, ct);

    public Task<OfflineLicensePermit?> FindPermitByIdAsync(Guid shopId, Guid permitId, CancellationToken ct) =>
        db.Set<OfflineLicensePermit>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.ShopId == shopId && row.Id == permitId, ct);

    public async Task<IReadOnlyList<LicenseGrantEvidence>> ReadGrantEvidenceAsync(Guid shopId, Guid accountId,
        Guid affiliationRequestId, CancellationToken ct) =>
        await db.Set<LicenseGrantEvidence>().FromSql($"""
            SELECT e.* FROM public.license_grant_evidence e
            JOIN public.entitlement_snapshot_inbox i USING (event_id, account_id, shop_id, digest)
            WHERE e.shop_id = {shopId} AND e.account_id = {accountId} AND e.affiliation_request_id = {affiliationRequestId}
                AND convert_from(i.acknowledgement, 'UTF8')::jsonb ->> 'outcome' = 'Applied'
            """).AsNoTracking()
            .OrderBy(row => row.AppliedAt)
            .ThenBy(row => row.EventId)
            .ToListAsync(ct);

    public async Task<ErrorOr<Success>> RetainKeyAsync(Guid shopId, string keyId, byte[] publicKey, DateTimeOffset now,
        CancellationToken ct)
    {
        var fingerprint = OfflinePermitSignatures.Fingerprint(publicKey);
        // A concurrent first use waits on the unique index, then both read the row that won.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.offline_license_verification_keys (id, shop_id, key_id, public_key, fingerprint, first_used_at)
            VALUES ({Guid.NewGuid()}, {shopId}, {keyId}, {publicKey}, {fingerprint}, {now})
            ON CONFLICT (shop_id, key_id) DO NOTHING
            """, ct);
        var retained = await db.Set<OfflineLicenseVerificationKey>().AsNoTracking()
            .Where(row => row.ShopId == shopId && row.KeyId == keyId).Select(row => row.Fingerprint).SingleOrDefaultAsync(ct);
        return retained == fingerprint ? Result.Success : PermitErrors.KeyRebound;
    }

    public Task<byte[]?> FindKeyAsync(Guid shopId, string keyId, CancellationToken ct) =>
        db.Set<OfflineLicenseVerificationKey>().AsNoTracking()
            .Where(row => row.ShopId == shopId && row.KeyId == keyId).Select(row => row.PublicKey).SingleOrDefaultAsync(ct)!;

    public async Task<bool> TryInsertPermitAsync(OfflineLicensePermit permit, CancellationToken ct) =>
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO public.offline_license_permits (id, shop_id, grant_id, lease_id, snapshot_id, user_id, session_id,
                device_id, till_session_id, batch_count, catalog_version, current_account_revision, current_shop_revision,
                completed_at, not_before, valid_until, limiting_reason, key_id, signed_permit)
            VALUES ({permit.Id}, {permit.ShopId}, {permit.GrantId}, {permit.LeaseId}, {permit.SnapshotId}, {permit.UserId},
                {permit.SessionId}, {permit.DeviceId}, {permit.TillSessionId}, {permit.BatchCount}, {permit.CatalogVersion},
                {permit.CurrentAccountRevision}, {permit.CurrentShopRevision}, {permit.CompletedAt}, {permit.NotBefore},
                {permit.ValidUntil}, {permit.LimitingReason}, {permit.KeyId}, {permit.SignedPermit})
            ON CONFLICT (shop_id, snapshot_id) DO NOTHING
            """, ct) == 1;
}
