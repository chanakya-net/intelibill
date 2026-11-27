using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Common.Licensing.Offline;
using ErrorOr;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using Errors = Intelibill.Application.Common.Errors.Errors;

namespace Intelibill.Application.Features.Sales.Commands.ReserveInvoiceLease;

public sealed class ReserveInvoiceLeaseCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    IInvoiceLeaseRepository invoiceLeaseRepository,
    IInvoiceNumberAllocator invoiceNumberAllocator,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IShopLicenceFence licenceFence,
    IOfflineLicencePermitIssuer permitIssuer)
{
    [LicenceAction(LicenceAction.NewTrading)]
    public async Task<ErrorOr<InvoiceLeaseDto>> HandleAsync(ReserveInvoiceLeaseCommand command, CancellationToken cancellationToken)
    {
        var actor = await userRepository.GetByIdWithDetailsAsync(command.ActorUserId, cancellationToken);
        if (actor is null)
            return Errors.Auth.UserNotFound;

        var membership = actor.ShopMemberships.FirstOrDefault(sm => sm.ShopId == command.ShopId);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.InvoiceLease.UserNotAuthorized;

        var blockSize = command.BlockSize ?? InvoiceLeaseDefaults.DefaultBlockSize;
        if (blockSize <= 0)
            return Errors.InvoiceLease.BlockSizeInvalid;
        if (blockSize > InvoiceLeaseDefaults.MaxBlockSize)
            return Errors.InvoiceLease.BlockSizeTooLarge;

        var shop = await shopRepository.GetByIdAsync(command.ShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        // A device issues documents offline under the identity frozen here, so a shop that could
        // not issue one online cannot reserve numbers either.
        var profileOrError = InvoiceDocumentBuilder.CaptureSellerProfile(shop);
        if (profileOrError.IsError)
            return profileOrError.Errors;

        var terms = BuildLeaseTerms(shop, profileOrError.Value, blockSize);

        var reservation = await ReserveInTransactionAsync(command, shop, terms, cancellationToken);
        if (reservation.IsError)
            return reservation.Errors;

        return ToDto(reservation.Value, terms.FiscalYear);
    }

    private LeaseTerms BuildLeaseTerms(Shop shop, InvoiceSellerProfile profile, int blockSize)
    {
        var reservedAt = OfflinePermitContractV1.Instant(timeProvider.GetUtcNow());
        var fiscalYear = FiscalYear.ForDate(reservedAt);

        // A lease never straddles two fiscal years: its numbers carry this year's series, so it stops
        // when the next year starts (1 April 00:00 IST, the business-calendar boundary).
        var fullTerm = reservedAt.AddDays(InvoiceLeaseDefaults.LeaseDurationDays);
        var expiresAt = fullTerm < fiscalYear.NextStartsAt ? fullTerm : fiscalYear.NextStartsAt;

        var series = new InvoiceSeriesKey(shop.Id, shop.SellerRegistrationKey, profile.SeriesCode, fiscalYear);

        return new LeaseTerms(series, fiscalYear, blockSize, profile, reservedAt, expiresAt);
    }

    private async Task<ErrorOr<LeaseReservation>> ReserveInTransactionAsync(
        ReserveInvoiceLeaseCommand command,
        Shop shop,
        LeaseTerms terms,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        var committed = false;
        try
        {
            var staged = await StageLeaseAsync(command, shop, terms, cancellationToken);
            if (staged.IsError)
                return staged.Errors;

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
            committed = true;
            return staged.Value;
        }
        finally
        {
            if (!committed)
                await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Runs inside the open transaction: trading fence first, then the number range, then the lease row
    /// and its licence evidence.
    /// </summary>
    private async Task<ErrorOr<LeaseReservation>> StageLeaseAsync(
        ReserveInvoiceLeaseCommand command,
        Shop shop,
        LeaseTerms terms,
        CancellationToken cancellationToken)
    {
        if (await licenceFence.ReadTradingDenialAsync(shop.Id, cancellationToken) is { } denied)
            return denied;

        var range = await invoiceNumberAllocator.ReserveRangeAsync(terms.Series, terms.BlockSize, cancellationToken);
        if (range.IsError)
            return range.Errors;

        var lease = InvoiceLease.Create(
            shop.Id,
            range.Value.SequenceId,
            command.DeviceId,
            terms.FiscalYear.StartYear,
            range.Value.Prefix,
            range.Value.Start,
            range.Value.End,
            InvoiceNumberFormat.CounterPadding,
            terms.ReservedAt,
            terms.ExpiresAt,
            command.ActorUserId,
            command.SessionId,
            terms.Profile);

        await invoiceLeaseRepository.AddAsync(lease, cancellationToken);

        var evidence = await CaptureLicenceEvidenceAsync(command, shop.Id, lease, cancellationToken);
        if (evidence.Refusal is { } refusal)
            return refusal;

        return new LeaseReservation(lease, evidence.Evidence);
    }

    private async Task<LicenceEvidenceOutcome> CaptureLicenceEvidenceAsync(
        ReserveInvoiceLeaseCommand command,
        Guid shopId,
        InvoiceLease lease,
        CancellationToken cancellationToken)
    {
        if (command.Session is null)
            return new LicenceEvidenceOutcome(null, null);

        var capture = await permitIssuer.CaptureGrantAsync(command.Session, lease, cancellationToken);
        if (capture.IsError)
        {
            var refusal = await ClassifyCaptureErrorAsync(shopId, capture.FirstError, cancellationToken);
            return new LicenceEvidenceOutcome(refusal, null);
        }

        var evidence = new InvoiceLeaseLicenceEvidenceDto(
            OfflinePermitContractV1.Version,
            capture.Value.GrantId,
            OfflinePermitContractV1.Digest(capture.Value.SignedGrant),
            capture.Value.KeyId);
        return new LicenceEvidenceOutcome(null, evidence);
    }

    private static InvoiceLeaseDto ToDto(LeaseReservation reservation, FiscalYear fiscalYear)
    {
        var lease = reservation.Lease;
        return new InvoiceLeaseDto(
            lease.Id,
            lease.ShopId,
            lease.DeviceId,
            fiscalYear.Label,
            lease.Prefix,
            lease.NumberPadding,
            lease.RangeStart,
            lease.RangeEnd,
            lease.NextNumber,
            lease.RemainingCount,
            lease.ReservedAt,
            lease.ExpiresAt,
            lease.SellerProfile,
            reservation.Evidence);
    }

    /// <summary>
    /// Applies the P05-01 legacy-fallback contract: null lets the unanchored lease commit with no
    /// licence evidence; a non-null error rolls the reservation back.
    /// </summary>
    private async Task<Error?> ClassifyCaptureErrorAsync(Guid shopId, Error error, CancellationToken cancellationToken)
    {
        if (error.Code == Errors.OfflinePermit.SigningUnavailable.Code ||
            error.Code == Errors.OfflinePermit.AuthorityUnavailable.Code)
            return null;

        if (error.Code.StartsWith("Licence.", StringComparison.Ordinal) &&
            error.Code != Errors.Licence.BusyCode)
            return await licenceFence.ReadTradingDenialAsync(shopId, cancellationToken);

        return error;
    }

    private sealed record LeaseTerms(
        InvoiceSeriesKey Series,
        FiscalYear FiscalYear,
        int BlockSize,
        InvoiceSellerProfile Profile,
        DateTimeOffset ReservedAt,
        DateTimeOffset ExpiresAt);

    private sealed record LeaseReservation(InvoiceLease Lease, InvoiceLeaseLicenceEvidenceDto? Evidence);

    private sealed record LicenceEvidenceOutcome(Error? Refusal, InvoiceLeaseLicenceEvidenceDto? Evidence);
}
