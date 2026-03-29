using FluentValidation;
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
    private readonly IValidator<RegisterWithEmailCommand> _validator = Substitute.For<IValidator<RegisterWithEmailCommand>>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RegisterWithEmailCommandHandler _handler;

    public RegisterWithEmailCommandHandlerTests()
    {
        _handler = new RegisterWithEmailCommandHandler(_validator, _userRepository, _refreshTokenRepository, _passwordHasher, _tokenService, _unitOfWork);
        _validator.ValidateAsync(Arg.Any<RegisterWithEmailCommand>(), Arg.Any<CancellationToken>())
                  .Returns(new FluentValidation.Results.ValidationResult());
    }

    [Fact]
    public async Task HandleAsync_EmailAlreadyInUse_ReturnsError()
    {
        var command = new RegisterWithEmailCommand("test@test.com", "Pass123!", "First", "Last");
        _userRepository.ExistsByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.EmailAlreadyInUse.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_RegistersUserAndReturnsAuthResult()
    {
        var command = new RegisterWithEmailCommand("test@test.com", "Pass123!", "First", "Last");
        _userRepository.ExistsByEmailAsync(command.Email, Arg.Any<CancellationToken>()).Returns(false);
        _passwordHasher.Hash(command.Password).Returns("hashedPassword");

        var expiry = DateTimeOffset.UtcNow.AddMinutes(15);
        _tokenService.GenerateAccessToken(Arg.Any<User>(), Arg.Any<Guid?>(), Arg.Any<string?>()).Returns(("accessToken", expiry));

        var refreshToken = Domain.Entities.RefreshToken.Create(Guid.NewGuid(), "refreshToken", DateTimeOffset.UtcNow.AddDays(7));
        _tokenService.CreateRefreshToken(Arg.Any<Guid>()).Returns(refreshToken);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("accessToken", result.Value.AccessToken);
        Assert.Equal("refreshToken", result.Value.RefreshToken);
        Assert.Equal("test@test.com", result.Value.User.Email);
        Assert.Equal("First", result.Value.User.FirstName);

        await _userRepository.Received(1).AddAsync(Arg.Is<User>(u => string.Equals(u.Email, command.Email, StringComparison.OrdinalIgnoreCase) && u.PasswordHash == "hashedPassword"), Arg.Any<CancellationToken>());
        await _refreshTokenRepository.Received(1).AddAsync(refreshToken, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
