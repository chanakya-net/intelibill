using FluentValidation;

namespace Intelibill.Application.Features.Exports.ProfitLoss.Queries.ExportProfitLoss;

public sealed class ExportProfitLossQueryValidator : AbstractValidator<ExportProfitLossQuery>
{
    private static readonly string[] AllowedTypes = ["all", "sale", "saleReturn", "inventoryAdjustment"];

    public ExportProfitLossQueryValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.ShopId)
            .NotEmpty();

        RuleFor(x => x.From)
            .NotEmpty();

        RuleFor(x => x.To)
            .NotEmpty();

        RuleFor(x => x.Format)
            .Must(BeSupportedFormat)
            .WithErrorCode("Exports.ProfitLoss.InvalidFormat")
            .WithMessage("'format' must be xlsx.");

        RuleFor(x => x.Type)
            .Must(BeAllowedType)
            .WithErrorCode("Exports.ProfitLoss.InvalidType")
            .WithMessage("'type' must be one of: all, sale, saleReturn, inventoryAdjustment.");

        RuleFor(x => x)
            .Must(HaveValidDateRange)
            .WithErrorCode("Exports.ProfitLoss.InvalidDateRange")
            .WithMessage("'from' date must be before or equal to 'to' date.");
    }

    private static bool BeSupportedFormat(string? format) =>
        string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase);

    private static bool BeAllowedType(string? type) =>
        type is null || AllowedTypes.Contains(type, StringComparer.OrdinalIgnoreCase);

    private static bool HaveValidDateRange(ExportProfitLossQuery query) =>
        query.From <= query.To;
}
