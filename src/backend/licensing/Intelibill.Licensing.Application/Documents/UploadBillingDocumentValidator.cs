using FluentValidation;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Documents;

public sealed partial class BillingDocumentMetadataValidator : AbstractValidator<BillingDocumentMetadata>
{
    public const int MaxBytes = 10 * 1024 * 1024;
    public const int MaxReasonLength = 400;
    public const string PdfMediaType = "application/pdf";
    public const string AccountingInvoice = "AccountingInvoice";
    public static readonly DateOnly EarliestIssueDate = new(2000, 1, 1);

    public BillingDocumentMetadataValidator()
    {
        RuleFor(x => x.Kind).Equal(AccountingInvoice);
        RuleFor(x => x.FileName).NotEmpty().Matches(FileNamePattern());
        RuleFor(x => x.MediaType).Equal(PdfMediaType);
        RuleFor(x => x.ByteLength).InclusiveBetween(1, MaxBytes);
        RuleFor(x => x.Sha256).Must(CheckoutOrdersV1.IsHash);
        RuleFor(x => x.IssuerName).Must(value => IsIssuerField(value, 200));
        RuleFor(x => x.IssuerReference).Must(value => IsIssuerField(value, 64));
        RuleFor(x => x.IssuedOn).GreaterThanOrEqualTo(EarliestIssueDate);
    }

    internal static bool IsReason(string? value) => IsIssuerField(value, MaxReasonLength);

    private static bool IsIssuerField(string? value, int max) => value is { Length: > 0 } && value.Length <= max
        && string.Equals(value, value.Trim(), StringComparison.Ordinal) && !value.Any(char.IsControl);

    [System.Text.RegularExpressions.GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9 ._()-]{0,114}\.[Pp][Dd][Ff]\z")]
    private static partial System.Text.RegularExpressions.Regex FileNamePattern();
}

public sealed class UploadBillingDocumentValidator : AbstractValidator<UploadBillingDocument>
{
    public UploadBillingDocumentValidator(BillingDocumentMetadataValidator metadata)
    {
        Include(metadata);
        RuleFor(x => x.OperationKey).NotEmpty();
        RuleFor(x => x.PreviewDigest).Must(CheckoutOrdersV1.IsHash);
        RuleFor(x => x.Confirmed).Equal(true);
    }
}

public sealed class SupersedeBillingDocumentMetadataValidator : AbstractValidator<SupersedeBillingDocumentMetadata>
{
    public SupersedeBillingDocumentMetadataValidator(BillingDocumentMetadataValidator metadata)
    {
        Include(metadata);
        RuleFor(x => x.Reason).Must(BillingDocumentMetadataValidator.IsReason);
    }
}

public sealed class SupersedeBillingDocumentValidator : AbstractValidator<SupersedeBillingDocument>
{
    public SupersedeBillingDocumentValidator(SupersedeBillingDocumentMetadataValidator metadata)
    {
        Include(metadata);
        RuleFor(x => x.OperationKey).NotEmpty();
        RuleFor(x => x.PreviewDigest).Must(CheckoutOrdersV1.IsHash);
        RuleFor(x => x.Confirmed).Equal(true);
    }
}
