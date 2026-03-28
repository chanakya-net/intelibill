using FluentValidation;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Users.Commands.UpdateMyProfile;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Users.Commands.UpdateMyProfile;

public class UpdateMyProfileCommandHandlerTests
{
    private readonly IValidator<UpdateMyProfileCommand> _validator = Substitute.For<IValidator<UpdateMyProfileCommand>>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokenRepository = Substitute.For<IRefreshTokenRepository>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly UpdateMyProfileCommandHandler _handler;

    public UpdateMyProfileCommandHandlerTests()
    {
        _handler = new UpdateMyProfileCommandHandler(
            _validator,
            _userRepository,
            _refreshTokenRepository,
            _tokenService,
            _unitOfWork);

        _validator.ValidateAsync(Arg.Any<UpdateMyProfileCommand>(), Arg.Any<CancellationToken>())
            .Returns(new FluentValidation.Results.ValidationResult());
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ReturnsNotFoundError()
    {
        var command = new UpdateMyProfileCommand(Guid.NewGuid(), "user@test.com", "+15551234567", "First", "Last");
        _userRepository.GetByIdWithDetailsAsync(command.UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.UserNotFound.Code);
    }

    [Fact]
    public async Task HandleAsync_EmailConflict_ReturnsConflictError()
    {
        var user = User.CreateWithEmail("current@test.com", "hash", "First", "Last");
        var command = new UpdateMyProfileCommand(user.Id, "taken@test.com", null, "First", "Last");

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepository.ExistsByEmailAsync("taken@test.com", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.EmailAlreadyInUse.Code);
    }

    [Fact]
    public async Task HandleAsync_PhoneConflict_ReturnsConflictError()
    {
        var user = User.CreateWithEmail("current@test.com", "hash", "First", "Last");
        var command = new UpdateMyProfileCommand(user.Id, "current@test.com", "+15551234567", "First", "Last");

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber!, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.PhoneAlreadyInUse.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_UpdatesProfileAndReturnsAuthResult()
    {
        var user = User.CreateWithEmail("current@test.com", "hash", "First", "Last");
        var command = new UpdateMyProfileCommand(user.Id, "updated@test.com", "+15551234567", "Updated", "User");

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepository.ExistsByEmailAsync("updated@test.com", Arg.Any<CancellationToken>()).Returns(false);
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber!, Arg.Any<CancellationToken>()).Returns(false);

        var refreshToken = RefreshToken.Create(user.Id, "refresh-token", DateTimeOffset.UtcNow.AddDays(7));
        _tokenService.GenerateAccessToken(user, Arg.Any<Guid?>()).Returns(("access-token", DateTimeOffset.UtcNow.AddMinutes(15)));
        _tokenService.CreateRefreshToken(user.Id).Returns(refreshToken);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.Equal("updated@test.com", result.Value.User.Email);
        Assert.Equal("+15551234567", result.Value.User.PhoneNumber);
        Assert.Equal("Updated", result.Value.User.FirstName);
        Assert.Equal("User", result.Value.User.LastName);

        _userRepository.Received(1).Update(user);
        await _refreshTokenRepository.Received(1).AddAsync(refreshToken, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
