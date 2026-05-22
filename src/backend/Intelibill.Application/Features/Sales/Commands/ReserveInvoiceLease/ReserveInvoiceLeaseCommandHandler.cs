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
    IInvoiceLeaseRepository invoiceLeaseRepository)
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

        var reservedAt = DateTimeOffset.UtcNow;
        var expiresAt = reservedAt.AddDays(InvoiceLeaseDefaults.LeaseDurationDays);
        var fiscalYear = FiscalYear.ForDate(reservedAt);

        var lease = await invoiceLeaseRepository.ReserveAsync(
            command.ShopId,
            command.DeviceId,
            fiscalYear.StartYear,
            fiscalYear.InvoicePrefix,
            blockSize,
            reservedAt,
            expiresAt,
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
