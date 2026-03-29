using Intelibill.Domain.Entities;
using Intelibill.Domain.Events;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Unit.Tests.Entities;

public class UserTests
{
    [Fact]
    public void CreateWithEmail_NormalizesEmailAndStoresPassword()
    {
        var user = User.CreateWithEmail("USER@Test.COM", "hash", "First", "Last");

        Assert.Equal("user@test.com", user.Email);
        Assert.Equal("hash", user.PasswordHash);
        Assert.Equal("First", user.FirstName);
        Assert.Equal("Last", user.LastName);
        Assert.False(user.IsEmailVerified);
    }

    [Fact]
    public void CreateWithPhone_SetsPhoneAndRaisesDomainEventWithoutEmail()
    {
        var user = User.CreateWithPhone("+15550001", "First", "Last");

        Assert.Equal("+15550001", user.PhoneNumber);
        Assert.Null(user.Email);

        var evt = Assert.IsType<UserRegisteredEvent>(Assert.Single(user.DomainEvents));
        Assert.Equal(user.Id, evt.UserId);
        Assert.Null(evt.Email);
    }

    [Fact]
    public void CreateFromExternalProvider_WithEmail_SetsEmailVerified()
    {
        var user = User.CreateFromExternalProvider("EXTERNAL@test.com", "First", "Last");

        Assert.Equal("external@test.com", user.Email);
        Assert.True(user.IsEmailVerified);
    }

    [Fact]
    public void AddExternalLogin_AddsToCollection()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        var externalLogin = UserExternalLogin.Create(user.Id, ExternalAuthProvider.Google, "provider-key", "ext@test.com");

        user.AddExternalLogin(externalLogin);

        var added = Assert.Single(user.ExternalLogins);
        Assert.Equal("provider-key", added.ProviderKey);
    }

    [Fact]
    public void AddShopMembership_AttachesMembershipToUser()
    {
        var user = User.CreateWithEmail("user@test.com", "hash", "First", "Last");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);
        var membership = ShopMembership.Create(shop.Id, user.Id, ShopRole.Owner, true);

        shop.AddMembership(membership);
        user.AddShopMembership(membership);

        var added = Assert.Single(user.ShopMemberships);
        Assert.Equal(user.Id, added.UserId);
        Assert.Equal(user.Id, added.User.Id);
        Assert.Equal(shop.Id, added.ShopId);
    }

    [Fact]
    public void UpdatePassword_ReplacesPasswordHash()
    {
        var user = User.CreateWithEmail("user@test.com", "old", "First", "Last");

        user.UpdatePassword("new-hash");

        Assert.Equal("new-hash", user.PasswordHash);
    }
}