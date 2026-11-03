using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Normalization;
using Intelibill.Application.Common.Operations;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase;

internal sealed class ResolveReconciliationCaseCommandValidator : AbstractValidator<ResolveReconciliationCaseCommand>
{
    public const int MaxReasonLength = 500;

    public ResolveReconciliationCaseCommandValidator()
    {
        RuleFor(x => x.OperationId)
            .Must(key => FinancialOperationHasher.NormalizeKey(key) is { Length: <= OperationReceipt.MaxKeyLength })
            .WithErrorCode(Errors.Operation.InvalidKey.Code)
            .WithMessage(Errors.Operation.InvalidKey.Description);

        RuleFor(x => x.Action)
            .Must(action => ParseAction(action) is not null)
            .WithErrorCode(Errors.Reconciliation.UnknownAction.Code)
            .WithMessage(Errors.Reconciliation.UnknownAction.Description);

        RuleFor(x => x.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithErrorCode(Errors.Reconciliation.ReasonRequired.Code)
            .WithMessage(Errors.Reconciliation.ReasonRequired.Description)
            .Must(reason => reason is null || reason.Trim().Length <= MaxReasonLength)
            .WithErrorCode(Errors.Reconciliation.ReasonRequired.Code)
            .WithMessage($"The reason must be {MaxReasonLength} characters or fewer.")
            .Must(StorableText.IsStorable)
            .WithErrorCode(Errors.Reconciliation.ReasonInvalid.Code)
            .WithMessage(Errors.Reconciliation.ReasonInvalid.Description);

        RuleFor(x => x.LinkedOperationId)
            .Must(id => id is { } linked && linked != Guid.Empty)
            .When(x => ParseAction(x.Action) is ReconciliationAction.LinkStockCorrection or ReconciliationAction.LinkExistingSale)
            .WithErrorCode(Errors.Reconciliation.LinkedOperationRequired.Code)
            .WithMessage(Errors.Reconciliation.LinkedOperationRequired.Description);
    }

    /// <summary>The action named exactly (ignoring case and surrounding space); numbers are not names.</summary>
    internal static ReconciliationAction? ParseAction(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmed = name.Trim();
        foreach (var action in Enum.GetValues<ReconciliationAction>())
        {
            if (string.Equals(action.ToString(), trimmed, StringComparison.OrdinalIgnoreCase))
                return action;
        }

        return null;
    }
}
