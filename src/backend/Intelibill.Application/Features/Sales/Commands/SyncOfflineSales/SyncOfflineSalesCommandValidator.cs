using FluentValidation;
using Intelibill.Application.Common.Errors;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

public sealed class SyncOfflineSalesCommandValidator : AbstractValidator<SyncOfflineSalesCommand>
{
    private const int MaxBatchSize = 50;

    public SyncOfflineSalesCommandValidator()
    {
        RuleFor(x => x.DeviceId)
            .NotEmpty()
            .WithErrorCode(Errors.InvoiceLease.DeviceIdRequired.Code)
            .WithMessage(Errors.InvoiceLease.DeviceIdRequired.Description);

        RuleFor(x => x.Sales)
            .NotEmpty()
            .WithErrorCode(Errors.Sale.OfflineSalesRequired.Code)
            .WithMessage(Errors.Sale.OfflineSalesRequired.Description);

        RuleFor(x => x.Sales)
            .Must(sales => sales.Count <= MaxBatchSize)
            .WithErrorCode(Errors.Sale.OfflineBatchLimitExceeded.Code)
            .WithMessage(Errors.Sale.OfflineBatchLimitExceeded.Description);
    }
}
