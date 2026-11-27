using FluentValidation;
using Intelibill.Domain.Common;
using Errors = Intelibill.Application.Common.Errors.Errors;

namespace Intelibill.Application.Features.Sales.Commands.ReserveInvoiceLease;

public sealed class ReserveInvoiceLeaseCommandValidator : AbstractValidator<ReserveInvoiceLeaseCommand>
{
    public ReserveInvoiceLeaseCommandValidator()
    {
        RuleFor(x => x.DeviceId)
            .NotEmpty()
            .WithErrorCode(Errors.InvoiceLease.DeviceIdRequired.Code)
            .WithMessage(Errors.InvoiceLease.DeviceIdRequired.Description);

        RuleFor(x => x.DeviceId)
            .MaximumLength(120);

        When(x => x.BlockSize.HasValue, () =>
        {
            RuleFor(x => x.BlockSize)
                .GreaterThan(0)
                .WithErrorCode(Errors.InvoiceLease.BlockSizeInvalid.Code)
                .WithMessage(Errors.InvoiceLease.BlockSizeInvalid.Description);

            RuleFor(x => x.BlockSize)
                .LessThanOrEqualTo(InvoiceLeaseDefaults.MaxBlockSize)
                .WithErrorCode(Errors.InvoiceLease.BlockSizeTooLarge.Code)
                .WithMessage(Errors.InvoiceLease.BlockSizeTooLarge.Description);
        });
    }
}
