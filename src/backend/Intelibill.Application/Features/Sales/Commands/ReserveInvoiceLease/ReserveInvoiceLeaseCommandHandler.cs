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
    TimeProvider timeProvider)
{
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

        var reservedAt = timeProvider.GetUtcNow();
        var fiscalYear = FiscalYear.ForDate(reservedAt);

        // A lease never straddles two fiscal years: its numbers carry this year's series, so it stops
        // when the next year starts (1 April 00:00 IST, the business-calendar boundary).
        var fullTerm = reservedAt.AddDays(InvoiceLeaseDefaults.LeaseDurationDays);
        var expiresAt = fullTerm < fiscalYear.NextStartsAt ? fullTerm : fiscalYear.NextStartsAt;

        var profile = profileOrError.Value;
        var series = new InvoiceSeriesKey(shop.Id, shop.SellerRegistrationKey, profile.SeriesCode, fiscalYear);

        InvoiceLease lease;
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var range = await invoiceNumberAllocator.ReserveRangeAsync(series, blockSize, cancellationToken);
            if (range.IsError)
            {
                await unitOfWork.RollbackTransactionAsync(cancellationToken);
                return range.Errors;
            }

            lease = InvoiceLease.Create(
                shop.Id,
                range.Value.SequenceId,
                command.DeviceId,
                fiscalYear.StartYear,
                range.Value.Prefix,
                range.Value.Start,
                range.Value.End,
                InvoiceNumberFormat.CounterPadding,
                reservedAt,
                expiresAt,
                command.ActorUserId,
                command.SessionId,
                profile);

            await invoiceLeaseRepository.AddAsync(lease, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(CancellationToken.None);
            throw;
        }

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
            lease.SellerProfile);
    }
}
