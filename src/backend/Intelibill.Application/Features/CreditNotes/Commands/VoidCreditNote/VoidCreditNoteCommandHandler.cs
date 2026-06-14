using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Normalization;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.CreditNotes.Commands.VoidCreditNote;

public sealed class VoidCreditNoteCommandHandler(
    IUserRepository userRepository,
    IShopRepository shopRepository,
    ICreditNoteRepository creditNoteRepository,
    IUnitOfWork unitOfWork)
{
    private static readonly VoidCreditNoteCommandValidator Validator = new();

    public async Task<ErrorOr<Success>> HandleAsync(
        VoidCreditNoteCommand command,
        CancellationToken cancellationToken)
    {
        var validation = Validator.Validate(command);
        if (!validation.IsValid)
        {
            return validation.Errors
                .Select(error => Error.Validation(error.ErrorCode, error.ErrorMessage))
                .ToList();
        }

        var user = await userRepository.GetByIdAsync(command.ActorUserId, cancellationToken);
        if (user is null)
            return Errors.Auth.UserNotFound;

        var shop = await shopRepository.GetByIdAsync(command.ActiveShopId, cancellationToken);
        if (shop is null)
            return Errors.Shop.ShopNotFound;

        var membership = await shopRepository.GetMembershipAsync(command.ActorUserId, command.ActiveShopId, cancellationToken);
        if (membership is null)
            return Errors.Shop.MembershipNotFound;

        if (membership.Role is not (ShopRole.Owner or ShopRole.Manager))
            return Errors.CreditNote.UserIsNotOwnerOrManager;

        var code = CreditNoteCodeNormalizer.Normalize(command.Code);
        var creditNote = await creditNoteRepository.GetByCodeWithRedemptionsAsync(command.ActiveShopId, code, cancellationToken);
        if (creditNote is null)
            return Errors.CreditNote.CreditNoteNotFound(command.Code);

        var voidResult = creditNote.Void(command.Reason);
        if (voidResult.IsError)
            return voidResult.Errors;

        creditNoteRepository.Update(creditNote);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
