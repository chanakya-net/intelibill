using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

using FluentValidation;
using Intelibill.Application.Common.Extensions;

namespace Intelibill.Application.Features.Auth.Commands.RegisterWithPhone;

public sealed class RegisterWithPhoneCommandHandler(
    IValidator<RegisterWithPhoneCommand> validator,
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(
        RegisterWithPhoneCommand command,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateCommandAsync(command, cancellationToken);
        if (validationResult is { IsError: true } err) return err.Errors;

        if (await userRepository.ExistsByPhoneAsync(command.PhoneNumber, cancellationToken))
            return Errors.Auth.PhoneAlreadyInUse;

        var user = User.CreateWithPhone(command.PhoneNumber, command.FirstName, command.LastName);
        var (activeShopId, activeShopRole, shops) = AuthShopSelection.Resolve(user);

        await userRepository.AddAsync(user, cancellationToken);

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
