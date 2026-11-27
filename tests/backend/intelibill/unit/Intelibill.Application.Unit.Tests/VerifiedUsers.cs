using Intelibill.Domain.Entities;
namespace Intelibill.Application.Unit.Tests;
internal static class VerifiedUsers
{
    internal static User CreateWithEmail(string email, string passwordHash, string firstName, string lastName, string? phoneNumber = null)
    {
        var user = User.CreateWithEmail(email, passwordHash, firstName, lastName, phoneNumber);
        user.VerifyEmail(email); return user;
    }
}
