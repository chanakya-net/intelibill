using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class UserExternalLoginTests
{
    [Fact]
    public void Create_SetsExpectedProperties()
    {
        var userId = Guid.NewGuid();

        var result = UserExternalLogin.Create(userId, ExternalAuthProvider.Google, "provider-key", "user@test.com");
        Assert.False(result.IsError);

        var login = result.Value;

        Assert.Equal(userId, login.UserId);
        Assert.Equal(ExternalAuthProvider.Google, login.Provider);
        Assert.Equal("provider-key", login.ProviderKey);
        Assert.Equal("user@test.com", login.ProviderEmail);
    }

    [Fact]
    public void Create_EmptyProviderKey_ReturnsValidationError()
    {
        var result = UserExternalLogin.Create(Guid.NewGuid(), ExternalAuthProvider.Google, "   ", "user@test.com");

        Assert.True(result.IsError);
        Assert.Equal("UserExternalLogin.ProviderKeyRequired", result.FirstError.Code);
    }
}
