using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Auth.Commands.RegisterWithEmail;

public sealed class RegisterWithEmailCommandHandler(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IUnitOfWork unitOfWork)
{
    public Task<ErrorOr<AuthResult>> HandleAsync(RegisterWithEmailCommand command, CancellationToken cancellationToken)
    {
        _ = (userRepository, refreshTokenRepository, passwordHasher, tokenService, unitOfWork, command, cancellationToken);
        return Task.FromResult<ErrorOr<AuthResult>>(Error.Conflict("Auth.EmailVerificationRequired", "Request mailbox verification before choosing your password."));
    }
}
