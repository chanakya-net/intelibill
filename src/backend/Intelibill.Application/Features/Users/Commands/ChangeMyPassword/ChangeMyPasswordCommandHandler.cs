using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.ChangeMyPassword;

public sealed class ChangeMyPasswordCommandHandler(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<bool>> HandleAsync(ChangeMyPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Auth.UserNotFound;

        if (string.IsNullOrWhiteSpace(user.PasswordHash))
            return Errors.Auth.PasswordNotSet;

        if (!passwordHasher.Verify(command.CurrentPassword, user.PasswordHash))
            return Errors.Auth.InvalidCurrentPassword;

        user.UpdatePassword(passwordHasher.Hash(command.NewPassword));
        userRepository.Update(user);

        await refreshTokenRepository.RevokeAllForUserAsync(user.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
