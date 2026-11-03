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
            INSERT INTO offline_submissions
                (id, shop_id, device_id, client_sale_id, invoice_number, payload_hash, payload_json,
                 claimed_sold_at, received_at, source, submitted_by_user_id, asserted_issuer_user_id,
                 original_issuer_user_id, original_lease_id, recovery_operation_id, sale_id, created_at)
            VALUES
                ({candidate.Id}, {candidate.ShopId}, {candidate.DeviceId}, {candidate.ClientSaleId},
                 {candidate.InvoiceNumber}, {candidate.PayloadHash}, CAST({candidate.PayloadJson} AS jsonb),
                 {candidate.ClaimedSoldAt}, {candidate.ReceivedAt}, {(int)candidate.Source},
                 {candidate.SubmittedByUserId}, {candidate.AssertedIssuerUserId},
                 {candidate.OriginalIssuerUserId}, {candidate.OriginalLeaseId}, {candidate.RecoveryOperationId},
                 {candidate.SaleId}, {candidate.CreatedAt})
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
