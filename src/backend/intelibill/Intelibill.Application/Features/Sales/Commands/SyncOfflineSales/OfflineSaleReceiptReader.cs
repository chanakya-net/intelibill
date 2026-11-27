using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

/// <summary>Reads the invoice lease and issuer standing a synced invoice's envelope is recorded and replayed under.</summary>
public sealed class OfflineSaleReceiptReader(
    IInvoiceLeaseRepository invoiceLeaseRepository,
    IShopRepository shopRepository,
    IOfflineSubmissionRepository offlineSubmissionRepository)
{
    /// <summary>Builds the envelope to record for the invoice, with the reserved range its number was found in.</summary>
    internal async Task<(OfflineSubmission Candidate, InvoiceLease? IncomingLease)> ReadCandidateAsync(
        SyncOfflineSalesCommand command,
        OfflineSaleSyncCommand sale,
        string deviceId,
        string normalizedId,
        string invNum,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var hash = OfflineSaleSyncIdempotencyHasher.ComputeHash(command.ShopId, deviceId, sale);
        var incomingLease = await invoiceLeaseRepository.FindReservedRangeAsync(command.ShopId, deviceId, invNum, cancellationToken);
        var (assertedIssuer, verifiedIssuer) = await ResolveIssuerAsync(command, deviceId, sale.IssuedByUserId, cancellationToken);

        var candidate = OfflineSubmission.Record(
            command.ShopId, deviceId, normalizedId, invNum, hash, OfflineSubmissionPayload.Serialize(sale),
            sale.SoldAt, now, OfflineSubmissionSource.Sync, command.ActorUserId,
            assertedIssuer, verifiedIssuer, incomingLease?.Id, recoveryOperationId: null);

        return (candidate, incomingLease);
    }

    internal async Task<InvoiceLease?> ResolveEffectiveLeaseAsync(
        Guid shopId,
        OfflineSubmission submission,
        InvoiceLease? incomingLease,
        CancellationToken cancellationToken)
    {
        if (submission.OriginalLeaseId is { } leaseId)
        {
            if (incomingLease is not null && incomingLease.Id == leaseId)
                return incomingLease;
            var original = await invoiceLeaseRepository.GetByIdAsync(leaseId, cancellationToken);
            if (original is not null && original.ShopId == shopId)
                return original;
        }

        return incomingLease ?? await invoiceLeaseRepository.FindReservedRangeAsync(
            shopId,
            submission.DeviceId,
            submission.InvoiceNumber,
            cancellationToken);
    }

    private async Task<(Guid? Asserted, Guid? Verified)> ResolveIssuerAsync(
        SyncOfflineSalesCommand command,
        string deviceId,
        Guid? issuedByUserId,
        CancellationToken cancellationToken)
    {
        if (issuedByUserId is not { } asserted)
            return (null, null);

        if (asserted == command.ActorUserId)
            return (asserted, asserted);

        var standing = await OfflineIssuerVerification.AssessAsync(
            shopRepository, offlineSubmissionRepository, command.ShopId, deviceId, asserted, cancellationToken);
        return (asserted, standing == OfflineIssuerStanding.Verified ? asserted : null);
    }
}
