using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Auth.DTOs;

namespace Intelibill.Application.Features.Auth.Commands.RegisterWithPhone;

/// <summary>
/// Closed. This route used to check that a number was unused and then immediately issue a session,
/// without ever proving that the registrant could receive a message at it. That produced an
/// account nobody could sign back into once the session was gone, and let anyone reserve someone
/// else's number. The route stays mapped so clients get a stable
/// <c>Auth.PhoneEnrollmentUnavailable</c> rather than a 404, and verified enrollment runs on the
/// phone challenge endpoints instead. Accounts created by it earlier keep their data and their
/// sessions.
/// </summary>
public sealed class RegisterWithPhoneCommandHandler
{
    public static Task<ErrorOr<AuthResult>> HandleAsync(
        RegisterWithPhoneCommand command,
        CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<AuthResult>>(Errors.Phone.EnrollmentUnavailable);
}
