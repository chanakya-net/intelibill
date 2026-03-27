using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class UserExternalLoginTests
{
    [Fact]
    public void Create_SetsExpectedProperties()
    {
        var userId = Guid.NewGuid();

        var login = UserExternalLogin.Create(userId, ExternalAuthProvider.Google, "provider-key", "user@test.com");

        Assert.Equal(userId, login.UserId);
        Assert.Equal(ExternalAuthProvider.Google, login.Provider);
        Assert.Equal("provider-key", login.ProviderKey);
        Assert.Equal("user@test.com", login.ProviderEmail);
    }
}