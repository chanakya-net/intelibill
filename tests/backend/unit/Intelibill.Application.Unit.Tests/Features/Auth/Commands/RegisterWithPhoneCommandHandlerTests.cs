using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.Commands.RegisterWithPhone;

namespace Intelibill.Application.Unit.Tests.Features.Auth.Commands.RegisterWithPhone;

/// <summary>
/// The contract changed deliberately. This route used to answer a valid request with a session on
/// a number nobody had proved; it now refuses every request with a stable code, and verified
/// enrollment happens on the phone challenge endpoints.
/// </summary>
public class RegisterWithPhoneCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_PhoneEnrollmentDisabled_DoesNotCreateUserOrTokens()
    {
        var result = await RegisterWithPhoneCommandHandler.HandleAsync(
            new RegisterWithPhoneCommand("+919876543210", "First", "Last"),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Phone.EnrollmentUnavailable.Code, result.FirstError.Code);
        Assert.Equal(ErrorOr.ErrorType.Failure, result.FirstError.Type);
    }

    [Fact]
    public async Task HandleAsync_AnyPhoneNumber_IsRefusedWithTheSameCode()
    {
        var first = await RegisterWithPhoneCommandHandler.HandleAsync(new RegisterWithPhoneCommand("+919876543210", "A", "B"), CancellationToken.None);
        var second = await RegisterWithPhoneCommandHandler.HandleAsync(new RegisterWithPhoneCommand("+14155552671", "C", "D"), CancellationToken.None);

        Assert.Equal(first.FirstError.Code, second.FirstError.Code);
    }
}
