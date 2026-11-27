using FluentValidation;

namespace Intelibill.Licensing.Application.Payments;

public sealed class ConfirmManualPaymentValidator : AbstractValidator<ConfirmManualPayment>
{
    public ConfirmManualPaymentValidator()
    {
        RuleFor(x => x.OperationKey).NotEmpty();
        RuleFor(x => x.ExpectedRevision).GreaterThan(0);
        RuleFor(x => x.SnapshotHash).NotEmpty().Matches("^[a-f0-9]{64}$");
        RuleFor(x => x.ReceivingAccountId).NotEmpty().MaximumLength(80);
        RuleFor(x => x.ConfigurationVersion).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Reference).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Evidence).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(400);
        RuleFor(x => x.AmountPaise).InclusiveBetween(1, 1_000_000_000_000);
        RuleFor(x => x.Currency).Equal("INR");
        RuleFor(x => x.ReceivedAt).Must(at => at.Offset == TimeSpan.Zero && at > DateTimeOffset.UnixEpoch);
    }
}
