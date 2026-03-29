using ErrorOr;

namespace Intelibill.Application.Common.Errors;

public static partial class Errors
{
    public static class Users
    {
        public static Error RoleNotSupported =>
            Error.Validation("Users.RoleNotSupported", "Role must be either Manager or SalesPerson.");

        public static Error UserAlreadyMember =>
            Error.Conflict("Users.UserAlreadyMember", "This user is already a member of the active shop.");
    }
}