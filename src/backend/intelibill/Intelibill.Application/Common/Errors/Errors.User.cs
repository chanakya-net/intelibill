using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class User
    {
        public static Error NotFound =>
            Error.NotFound("User.NotFound", "User not found.");
    }
}
