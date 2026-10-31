using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Users.Commands.UpdateMyProfile;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Users.Commands.UpdateMyProfile;

public class UpdateMyProfileCommandHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly UpdateMyProfileCommandHandler _handler;

    public UpdateMyProfileCommandHandlerTests()
    {
        _handler = new UpdateMyProfileCommandHandler(_userRepository, _unitOfWork);
    }

    [Fact]
    public async Task HandleAsync_UserNotFound_ReturnsNotFoundError()
    {
        var command = new UpdateMyProfileCommand(Guid.NewGuid(), "user@test.com", "+15551234567", "First", "Last", SecurityVersion: 0);
        _userRepository.GetByIdWithDetailsAsync(command.UserId, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.UserNotFound.Code);
    }

    [Fact]
    public async Task HandleAsync_EmailConflict_ReturnsConflictError()
    {
        var user = User.CreateWithEmail("current@test.com", "hash", "First", "Last");
        var command = new UpdateMyProfileCommand(user.Id, "taken@test.com", null, "First", "Last", SecurityVersion: user.SecurityVersion);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepository.ExistsByEmailAsync("taken@test.com", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == "Auth.EmailChangeRequired");
    }

    [Fact]
    public async Task HandleAsync_PhoneConflict_ReturnsConflictError()
    {
        var user = User.CreateWithEmail("current@test.com", "hash", "First", "Last");
        var command = new UpdateMyProfileCommand(user.Id, "current@test.com", "+15551234567", "First", "Last", SecurityVersion: user.SecurityVersion);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber!, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Auth.PhoneAlreadyInUse.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_UpdatesProfileAndReturnsTheSavedProfile()
    {
        var user = User.CreateWithEmail("current@test.com", "hash", "First", "Last");
        var command = new UpdateMyProfileCommand(user.Id, "current@test.com", "+15551234567", "Updated", "User", SecurityVersion: user.SecurityVersion);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _userRepository.ExistsByEmailAsync("current@test.com", Arg.Any<CancellationToken>()).Returns(false);
        _userRepository.ExistsByPhoneAsync(command.PhoneNumber!, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // The saved profile and nothing else: editing a name is not a credential event, so it
        // produces no token and no session.
        Assert.False(result.IsError);
        Assert.Equal("current@test.com", result.Value.Email);
        Assert.Equal("+15551234567", result.Value.PhoneNumber);
        Assert.Equal("Updated", result.Value.FirstName);
        Assert.Equal("User", result.Value.LastName);

        _userRepository.Received(1).Update(user);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A proved number is a sign-in factor, and this request carries no proof of the new one. It is
    /// refused out loud rather than silently ignored, so nobody is left believing it moved.
    /// </summary>
    [Fact]
    public async Task HandleAsync_VerifiedPhone_RefusesToMoveItWithoutProof()
    {
        var user = User.CreateWithPhone("+919876543210", "Phone", "Owner");
        user.VerifyPhone("+919876543210", DateTimeOffset.UtcNow);
        var command = new UpdateMyProfileCommand(user.Id, null, "+919000000000", "Phone", "Owner", SecurityVersion: user.SecurityVersion);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(result.Errors, e => e.Code == Errors.Phone.ChangeRequiresProof.Code);
        Assert.Equal("+919876543210", user.PhoneNumber);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_VerifiedPhoneUnchanged_StillSavesOtherFields()
    {
        var user = User.CreateWithPhone("+919876543210", "Phone", "Owner");
        user.VerifyPhone("+919876543210", DateTimeOffset.UtcNow);
        var command = new UpdateMyProfileCommand(user.Id, null, "+919876543210", "Updated", "Owner", SecurityVersion: user.SecurityVersion);

        _userRepository.GetByIdWithDetailsAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var result = await _handler.HandleAsync(command, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Updated", result.Value.FirstName);
        Assert.Equal("+919876543210", result.Value.PhoneNumber);
    }
}
