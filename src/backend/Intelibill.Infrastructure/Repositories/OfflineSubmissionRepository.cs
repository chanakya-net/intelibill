using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class OfflineSubmissionRepository(ApplicationDbContext context)
    : RepositoryBase<OfflineSubmission>(context), IOfflineSubmissionRepository
{
    public async Task<OfflineSubmission> RecordAsync(OfflineSubmission candidate, CancellationToken cancellationToken)
    {
        // Runs in whatever transaction is open (or autocommits): an identical resend keeps the first row.
        await Context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH receipt AS (SELECT CAST({candidate.PayloadJson} AS jsonb) AS payload),
            matched AS (
                SELECT p.id, p.grant_id
                FROM receipt r
                JOIN offline_license_permits p ON p.shop_id = {candidate.ShopId} AND p.lease_id = {candidate.OriginalLeaseId}
                JOIN offline_license_grants g ON g.id = p.grant_id AND g.shop_id = p.shop_id
                WHERE r.payload->'licenceEvidence'->'version' = '1'::jsonb
                  AND p.signed_permit = r.payload->'licenceEvidence'->>'signedPermit'
                  AND p.key_id = r.payload->'licenceEvidence'->>'keyId'
                  AND p.id::text = r.payload->'licenceEvidence'->>'permitId'
                  AND p.lease_id::text = r.payload->'licenceEvidence'->>'leaseId'
                  AND p.snapshot_id::text = r.payload->'licenceEvidence'->>'snapshotId'
                  AND g.id::text = r.payload->'licenceEvidence'->>'grantId'
                  AND encode(sha256(convert_to(g.signed_grant, 'UTF8')), 'hex') = r.payload->'licenceEvidence'->>'grantSha256'
            )
            INSERT INTO offline_submissions
                (id, shop_id, device_id, client_sale_id, invoice_number, payload_hash, payload_json,
                 claimed_sold_at, received_at, source, submitted_by_user_id, asserted_issuer_user_id,
                 original_issuer_user_id, original_lease_id, recovery_operation_id, sale_id, created_at, licence_permit_id, licence_grant_id)
            SELECT {candidate.Id}, {candidate.ShopId}, {candidate.DeviceId}, {candidate.ClientSaleId},
                 {candidate.InvoiceNumber}, {candidate.PayloadHash}, receipt.payload,
                 {candidate.ClaimedSoldAt}, {candidate.ReceivedAt}, {(int)candidate.Source},
                 {candidate.SubmittedByUserId}, {candidate.AssertedIssuerUserId},
                 {candidate.OriginalIssuerUserId}, {candidate.OriginalLeaseId}, {candidate.RecoveryOperationId},
                 {candidate.SaleId}, {candidate.CreatedAt}, matched.id, matched.grant_id
            FROM receipt LEFT JOIN matched ON true
            ON CONFLICT (shop_id, device_id, client_sale_id, payload_hash) DO NOTHING
            """,
            cancellationToken);

        return await DbSet.SingleAsync(
            s => s.ShopId == candidate.ShopId
                && s.DeviceId == candidate.DeviceId
                && s.ClientSaleId == candidate.ClientSaleId
                && s.PayloadHash == candidate.PayloadHash,
            cancellationToken);
    }

    public async Task<OfflineSubmission?> FindEarliestByHashAsync(
        Guid shopId,
        string deviceId,
        string clientSaleId,
        string payloadHash,
        CancellationToken cancellationToken) =>
        await DbSet
            .AsNoTracking()
            .Where(s => s.ShopId == shopId
                && s.DeviceId == deviceId
                && s.ClientSaleId == clientSaleId
                && s.PayloadHash == payloadHash)
            .OrderBy(s => s.ReceivedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> HasDeviceHistoryAsync(
        Guid shopId,
        string deviceId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var reserved = await Context.InvoiceLeases
            .AsNoTracking()
            .AnyAsync(
                l => l.ShopId == shopId && l.DeviceId == deviceId && l.ReservedByUserId == userId,
                cancellationToken);
        if (reserved)
            return true;

        return await DbSet
            .AsNoTracking()
            .AnyAsync(
                s => s.ShopId == shopId
                    && s.DeviceId == deviceId
                    && s.Source == OfflineSubmissionSource.Sync
                    && s.SubmittedByUserId == userId,
                cancellationToken);
    }
}
