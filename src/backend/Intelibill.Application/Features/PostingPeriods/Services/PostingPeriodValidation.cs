using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.PostingPeriods.Services;

internal static class PostingPeriodValidation
{
    public static IRuleBuilderOptions<T, DateOnly> MustStartAMonth<T>(this IRuleBuilder<T, DateOnly> rule) =>
        rule.Must(date => date.Day == 1)
            .WithErrorCode(Errors.PostingPeriod.PeriodStartInvalid.Code)
            .WithMessage(Errors.PostingPeriod.PeriodStartInvalid.Description);

    public static IRuleBuilderOptions<T, string> MustBeAReason<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(reason =>
            {
                var length = reason?.Trim().Length ?? 0;
                return length is > 0 and <= PostingPeriod.MaxReasonLength;
            })
            .WithErrorCode(Errors.PostingPeriod.ReasonRequired.Code)
            .WithMessage(Errors.PostingPeriod.ReasonRequired.Description);

    public static IRuleBuilderOptions<T, string> MustBeAnOperationKey<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(key => !string.IsNullOrWhiteSpace(key))
            .WithErrorCode(Errors.PostingPeriod.OperationKeyRequired.Code)
            .WithMessage(Errors.PostingPeriod.OperationKeyRequired.Description)
            .Must(key => key is null || key.Trim().Length <= OperationReceipt.MaxKeyLength)
            .WithErrorCode(Errors.Operation.InvalidKey.Code)
            .WithMessage(Errors.Operation.InvalidKey.Description);
}
