using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Auth.Commands.RegisterWithPhone;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.RegisterWithPhone;

public class RegisterWithPhoneCommandHandlerTests
{
    private readonly IValidator<RegisterWithPhoneCommand> _validator = Substitute.For<IValidator<RegisterWithPhoneCommand>>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly RegisterWithPhoneCommandHandler _handler;

    public RegisterWithPhoneCommandHandlerTests()
    {
        _handler = new RegisterWithPhoneCommandHandler(_validator, _userRepository, _refreshTokenRepository, _tokenService, _unitOfWork);
        _validator.ValidateAsync(Arg.Any<RegisterWithPhoneCommand>(), Arg.Any<CancellationToken>())
                  .Returns(new FluentValidation.Results.ValidationResult());
    }

    [Fact]
    public async Task HandleAsync_PhoneAlreadyInUse_ReturnsError()
    {
        var command = new RegisterWithPhoneCommand("+1234567890", "First", "Last");
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.PhoneAlreadyInUse.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_RegistersUserAndReturnsAuthResult()
    {
        var command = new RegisterWithPhoneCommand("+1234567890", "First", "Last");
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber, Arg.Any<CancellationToken>()).Returns(false);
        _tokenService.GenerateAccessToken(Arg.Any<User>(), Arg.Any<Guid?>(), Arg.Any<string?>()).Returns(("accessToken", DateTimeOffset.UtcNow.AddMinutes(15)));

        var refreshToken = Domain.Entities.RefreshToken.Create(Guid.NewGuid(), "refreshToken", DateTimeOffset.UtcNow.AddDays(7));
        _tokenService.CreateRefreshToken(Arg.Any<Guid>()).Returns(refreshToken);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("accessToken", result.Value.AccessToken);
        Assert.Equal("refreshToken", result.Value.RefreshToken);
        Assert.Equal("+1234567890", result.Value.User.PhoneNumber);

        await _userRepository.Received(1).AddAsync(Arg.Is<User>(u => u.PhoneNumber == command.PhoneNumber), Arg.Any<CancellationToken>());
        await _refreshTokenRepository.Received(1).AddAsync(refreshToken, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
