using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.UpdateMyProfile;

/// <summary>
/// Saves the caller's own name, phone number and language. It returns the saved profile and nothing
/// else: editing a profile is not a credential event, and the session the request arrived on
/// carries on unchanged.
/// </summary>
public sealed class UpdateMyProfileCommandHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<UserDto>> HandleAsync(
        UpdateMyProfileCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Auth.UserNotFound;
        if (user.SecurityVersion != command.SecurityVersion || !user.IsLoginEnabled)
            return Errors.Auth.SessionInvalidated;

        var normalizedEmail = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email.Trim().ToLowerInvariant();
        var normalizedPhone = string.IsNullOrWhiteSpace(command.PhoneNumber)
            ? null
            : command.PhoneNumber.Trim();

        var emailChanged = !string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase);
        if (emailChanged)
            return Error.Conflict("Auth.EmailChangeRequired", "Use verified email change to replace a sign-in address.");

        var phoneChanged = !string.Equals(user.PhoneNumber, normalizedPhone, StringComparison.Ordinal);

        // A proved number is a sign-in factor. Saying so here, rather than quietly keeping the old
        // value, is the difference between a user who knows to use the phone change flow and one
        // who believes their number moved.
        if (phoneChanged && user.IsPhoneVerified)
            return Errors.Phone.ChangeRequiresProof;

        if (phoneChanged && normalizedPhone is not null && await userRepository.ExistsByPhoneAsync(normalizedPhone, cancellationToken))
            return Errors.Auth.PhoneAlreadyInUse;

        user.UpdateProfile(normalizedEmail ?? string.Empty, normalizedPhone, command.FirstName, command.LastName, command.Language);
        userRepository.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName, user.Language);
    }
}
