using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.RegisterWithEmail;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.RegisterWithEmail;

public class RegisterWithEmailCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RegisterWithEmailCommandHandler _handler;

    public RegisterWithEmailCommandHandlerTests()
    {
        _handler = new RegisterWithEmailCommandHandler(_userRepository, _refreshTokenRepository, _passwordHasher, _tokenService, _unitOfWork);
    }

    [Fact]
    public async Task LegacyCommand_CannotCreateCredentialsOrSession()
    {
        var result = await _handler.HandleAsync(new RegisterWithEmailCommand("victim@test.com", "Attacker!1", "First", "Last", "+1234567890"), CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Equal("Auth.EmailVerificationRequired", result.FirstError.Code);
        await _userRepository.DidNotReceive().AddAsync(Arg.Any<User>(), Arg.Any<CancellationToken>());
        await _refreshTokenRepository.DidNotReceive().AddAsync(Arg.Any<Intelibill.Domain.Entities.RefreshToken>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
