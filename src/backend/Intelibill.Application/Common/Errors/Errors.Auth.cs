using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Auth
    {
        public static Error EmailAlreadyInUse =>
            Error.Conflict("Auth.EmailAlreadyInUse", "An account with this email already exists.");

        public static Error PhoneAlreadyInUse =>
            Error.Conflict("Auth.PhoneAlreadyInUse", "An account with this phone number already exists.");

        public static Error InvalidCredentials =>
            Error.Unauthorized("Auth.InvalidCredentials", "The email or password is incorrect.");

        public static Error InvalidCurrentPassword =>
            Error.Unauthorized("Auth.InvalidCurrentPassword", "The current password is incorrect.");

        public static Error PasswordNotSet =>
            Error.Validation("Auth.PasswordNotSet", "This account does not have a password set.");

        public static Error UserNotFound =>
            Error.NotFound("Auth.UserNotFound", "User not found.");

        public static Error InvalidRefreshToken =>
            Error.Unauthorized("Auth.InvalidRefreshToken", "The refresh token is invalid or has expired.");

        public static Error InvalidPasswordResetToken =>
            Error.Unauthorized("Auth.InvalidPasswordResetToken", "The password reset link is invalid or has expired.");

        public static Error UnsupportedProvider =>
            Error.Validation("Auth.UnsupportedProvider", "The specified authentication provider is not supported.");

        public static Error ExternalProviderError(string description) =>
            Error.Unauthorized("Auth.ExternalProviderError", description);
    }
}
