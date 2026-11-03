using ErrorOr;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.ValueObjects;
using System.Linq;
using Errors = Intelibill.Application.Common.Errors.Errors;

namespace Intelibill.Application.Features.Sales.Commands.ReserveInvoiceLease;

public sealed class ReserveInvoiceLeaseCommandHandler(
    IUserRepository userRepository,
    IInvoiceLeaseRepository invoiceLeaseRepository,
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

        var reservedAt = timeProvider.GetUtcNow();
        var fiscalYear = FiscalYear.ForDate(reservedAt);

        // A lease never straddles two fiscal years: its numbers carry this year's prefix, so it stops
        // when the next year starts (1 April 00:00 UTC, the boundary FiscalYear.ForDate uses).
        var nextFiscalYearStart = new DateTimeOffset(fiscalYear.EndYear, InvoiceLeaseDefaults.FiscalYearStartMonth, 1, 0, 0, 0, TimeSpan.Zero);
        var fullTerm = reservedAt.AddDays(InvoiceLeaseDefaults.LeaseDurationDays);
        var expiresAt = fullTerm < nextFiscalYearStart ? fullTerm : nextFiscalYearStart;

        var lease = await invoiceLeaseRepository.ReserveAsync(
            command.ShopId,
            command.DeviceId,
            fiscalYear.StartYear,
            fiscalYear.InvoicePrefix,
            blockSize,
            reservedAt,
            expiresAt,
            command.ActorUserId,
            command.SessionId,
            cancellationToken);

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
            lease.ExpiresAt);
    }
}
