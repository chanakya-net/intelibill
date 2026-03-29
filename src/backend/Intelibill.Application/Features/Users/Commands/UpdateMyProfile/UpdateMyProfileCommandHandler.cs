using ErrorOr;
using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Extensions;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Users.Commands.UpdateMyProfile;

public sealed class UpdateMyProfileCommandHandler(
    IValidator<UpdateMyProfileCommand> validator,
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(
        UpdateMyProfileCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is { IsError: true } err) return err.Errors;

        var user = await userRepository.GetByIdWithDetailsAsync(command.UserId, cancellationToken);
        if (user is null)
            return Errors.Auth.UserNotFound;

        var normalizedEmail = command.Email.Trim().ToLowerInvariant();
        var normalizedPhone = string.IsNullOrWhiteSpace(command.PhoneNumber)
            ? null
            : command.PhoneNumber.Trim();

        var emailChanged = !string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase);
        if (emailChanged && await userRepository.ExistsByEmailAsync(normalizedEmail, cancellationToken))
            return Errors.Auth.EmailAlreadyInUse;

        var phoneChanged = !string.Equals(user.PhoneNumber, normalizedPhone, StringComparison.Ordinal);
        if (phoneChanged && normalizedPhone is not null && await userRepository.ExistsByPhoneAsync(normalizedPhone, cancellationToken))
            return Errors.Auth.PhoneAlreadyInUse;

        user.UpdateProfile(normalizedEmail, normalizedPhone, command.FirstName, command.LastName);
        userRepository.Update(user);

        var (activeShopId, activeShopRole, shops) = AuthShopSelection.Resolve(user);
        var (accessToken, accessTokenExpiry) = tokenService.GenerateAccessToken(user, activeShopId, activeShopRole);
        var refreshToken = tokenService.CreateRefreshToken(user.Id);

        await refreshTokenRepository.AddAsync(refreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthResult(
            accessToken,
            refreshToken.Token,
            accessTokenExpiry,
            refreshToken.ExpiresAt,
            new UserDto(user.Id, user.Email, user.PhoneNumber, user.FirstName, user.LastName),
            activeShopId,
            shops);
    }
}
