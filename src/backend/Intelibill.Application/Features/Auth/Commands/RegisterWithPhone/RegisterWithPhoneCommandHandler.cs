using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Commands.RegisterWithPhone;

public sealed class RegisterWithPhoneCommandHandler(
    IUserRepository userRepository,
    IAuthSessionRepository sessionRepository,
    IAuthSessionService sessions)
{
    public async Task<ErrorOr<AuthResult>> HandleAsync(
        RegisterWithPhoneCommand command,
        CancellationToken cancellationToken)
    {
        // The account, its first session and that session's refresh family are inserted together:
        // a half-registered account that cannot sign in is worse than a rejected registration.
        await using var transaction = await sessionRepository.BeginAsync(cancellationToken);

        if (await userRepository.ExistsByPhoneAsync(command.PhoneNumber, cancellationToken))
            return Errors.Auth.PhoneAlreadyInUse;

        var user = User.CreateWithPhone(command.PhoneNumber, command.FirstName, command.LastName);
        await userRepository.AddAsync(user, cancellationToken);

        var result = await sessions.IssueWithinTransactionAsync(user, preferredShopId: null, cancellationToken);

        return await sessionRepository.CommitAsync(cancellationToken)
            ? result
            : Errors.Auth.PhoneAlreadyInUse;
    }
}
