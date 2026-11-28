using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using static Intelibill.Application.Features.OfflineReconciliation.Commands.SubmitRecoveryBundle.SubmitRecoveryBundleCommandHandler;

namespace Intelibill.Application.Features.OfflineReconciliation.Commands.SubmitRecoveryBundle;

/// <summary>
/// Decides whether a recovered invoice is received (identity, structure, owned lease, issuer), records
/// its submission, and resolves the lease it is reviewed under.
/// </summary>
public sealed class RecoveryInvoiceReceiver(
    IOfflineSalePoster salePoster,
    IInvoiceLeaseRepository invoiceLeaseRepository,
    IShopRepository shopRepository,
    IOfflineSubmissionRepository submissionRepository)
{
    /// <summary>
    /// Checks the invoice's identity, structure, lease and issuer, then records the submission.
    /// A rejection is returned as its result; otherwise the recorded submission and incoming lease.
    /// </summary>
    internal async Task<(ReceivedInvoice? Received, RecoveryInvoiceResultDto? Rejection)> ReceiveSubmissionAsync(
        RecoveryContext context,
        OfflineSaleSyncCommand sale,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (OfflineSaleSyncHelpers.CheckIdentity(sale, out var clientSaleId, out var invoiceNumber) is { } identityError)
            return (null, Rejected(clientSaleId, invoiceNumber, identityError));

        var structure = salePoster.CheckStructure(sale);
        if (structure.IsError)
            return (null, Rejected(clientSaleId, invoiceNumber, structure.FirstError));

        var incomingLease = await invoiceLeaseRepository.FindReservedRangeAsync(context.ShopId, context.DeviceId, invoiceNumber, cancellationToken);
        if (incomingLease is null)
            return (null, Rejected(clientSaleId, invoiceNumber, Errors.Recovery.LeaseNotOwned));

        Guid? verifiedIssuer = null;
        if (sale.IssuedByUserId is { } asserted)
        {
            var standing = await OfflineIssuerVerification.AssessAsync(
                shopRepository, submissionRepository, context.ShopId, context.DeviceId, asserted, cancellationToken);
            if (standing == OfflineIssuerStanding.NeverMember)
                return (null, Rejected(clientSaleId, invoiceNumber, Errors.Recovery.IssuerMismatch));
            if (standing == OfflineIssuerStanding.Verified)
                verifiedIssuer = asserted;
        }

        var hash = OfflineSaleSyncIdempotencyHasher.ComputeHash(context.ShopId, context.DeviceId, sale);
        var submission = await submissionRepository.RecordAsync(
            OfflineSubmission.Record(
                context.ShopId, context.DeviceId, clientSaleId, invoiceNumber, hash,
                OfflineSubmissionPayload.Serialize(sale), sale.SoldAt, now, OfflineSubmissionSource.Recovery,
                context.OperatorUserId, sale.IssuedByUserId, verifiedIssuer, incomingLease.Id, context.OperationKey),
            cancellationToken);

        return (new ReceivedInvoice(submission, incomingLease), null);
    }

    internal async Task<InvoiceLease> ResolveEffectiveLeaseAsync(
        Guid shopId,
        OfflineSubmission submission,
        InvoiceLease incomingLease,
        CancellationToken cancellationToken)
    {
        if (submission.OriginalLeaseId is { } leaseId && leaseId != incomingLease.Id)
        {
            var original = await invoiceLeaseRepository.GetByIdAsync(leaseId, cancellationToken);
            if (original is not null && original.ShopId == shopId)
                return original;
        }
        return incomingLease;
    }

    private static RecoveryInvoiceResultDto Rejected(string clientSaleId, string invoiceNumber, Error error) =>
        new(clientSaleId, invoiceNumber, RecoveryInvoiceStatus.Rejected, null, null, [new OfflineSaleSyncErrorDto(error.Code, error.Description)]);
}
