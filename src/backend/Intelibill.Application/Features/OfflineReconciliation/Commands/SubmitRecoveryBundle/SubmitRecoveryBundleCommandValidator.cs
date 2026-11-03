using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Normalization;
using Intelibill.Application.Common.Operations;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.OfflineReconciliation.Commands.SubmitRecoveryBundle;

/// <summary>
/// The shape checks that need no database: a usable operation key and a bundle with a device id
/// that fits its column (and holds only text PostgreSQL can store), a checksum and an invoices array to compare it with. Format, shop and
/// checksum agreement are the handler's, so each answers with its own code.
/// </summary>
internal sealed class SubmitRecoveryBundleCommandValidator : AbstractValidator<SubmitRecoveryBundleCommand>
{
    private const int MaxDeviceIdLength = 120;

    public SubmitRecoveryBundleCommandValidator()
    {
        RuleFor(x => x.OperationId)
            .Must(key => FinancialOperationHasher.NormalizeKey(key) is { Length: <= OperationReceipt.MaxKeyLength })
            .WithErrorCode(Errors.Operation.InvalidKey.Code)
            .WithMessage(Errors.Operation.InvalidKey.Description);

        RuleFor(x => x.Bundle)
            .NotNull()
            .WithErrorCode(Errors.Recovery.BundleInvalid.Code)
            .WithMessage(Errors.Recovery.BundleInvalid.Description)
            .DependentRules(() =>
            {
                RuleFor(x => x.Bundle.DeviceId)
                    .Must(deviceId => !string.IsNullOrWhiteSpace(deviceId)
                        && deviceId.Trim().Length <= MaxDeviceIdLength
                        && StorableText.IsStorable(deviceId))
                    .WithErrorCode(Errors.Recovery.BundleInvalid.Code)
                    .WithMessage(Errors.Recovery.BundleInvalid.Description);

                RuleFor(x => x.Bundle.Format)
                    .NotNull()
                    .WithErrorCode(Errors.Recovery.BundleInvalid.Code)
                    .WithMessage(Errors.Recovery.BundleInvalid.Description);

                RuleFor(x => x.Bundle.Checksum)
                    .NotEmpty()
                    .WithErrorCode(Errors.Recovery.BundleInvalid.Code)
                    .WithMessage(Errors.Recovery.BundleInvalid.Description);

                RuleFor(x => x.Bundle.InvoicesJson)
                    .NotEmpty()
                    .WithErrorCode(Errors.Recovery.BundleInvalid.Code)
                    .WithMessage(Errors.Recovery.BundleInvalid.Description);
            });
    }
}
