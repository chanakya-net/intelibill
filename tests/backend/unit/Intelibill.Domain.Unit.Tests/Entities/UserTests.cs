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

public class UserVerifiedPhoneTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static User WithVerifiedPhone()
    {
        var user = User.CreateWithPhone("+919876543210", "Phone", "Owner");
        user.VerifyPhone("+919876543210", Now);
        return user;
    }

    [Fact]
    public void CreateWithPhone_DoesNotMarkTheNumberVerified()
    {
        var user = User.CreateWithPhone("+919876543210", "Phone", "Owner");

        Assert.False(user.IsPhoneVerified);
        Assert.Null(user.PhoneVerifiedAt);
    }

    [Fact]
    public void VerifyPhone_BindsTheFactorAndInvalidatesExistingSessions()
    {
        var user = User.CreateWithPhone("+919876543210", "Phone", "Owner");
        var before = user.SecurityVersion;

        user.VerifyPhone("+919876543210", Now);

        Assert.True(user.IsPhoneVerified);
        Assert.Equal(Now, user.PhoneVerifiedAt);
        Assert.Equal(before + 1, user.SecurityVersion);
    }

    /// <summary>
    /// Claiming an account with a mailbox drops its phone number. Leaving the verified flag behind
    /// would have the account claim a proved factor for a number it no longer holds.
    /// </summary>
    [Fact]
    public void ClaimEmail_ClearsVerifiedPhone()
    {
        var user = WithVerifiedPhone();

        user.ClaimEmail("owner@test.com", "hash", "Real", "Owner");

        Assert.Null(user.PhoneNumber);
        Assert.False(user.IsPhoneVerified);
        Assert.Null(user.PhoneVerifiedAt);
    }

    [Fact]
    public void UpdateProfile_CannotMoveAVerifiedPhone()
    {
        var user = WithVerifiedPhone();

        user.UpdateProfile("owner@test.com", "+919000000000", "Real", "Owner", "en-IN");

        Assert.Equal("+919876543210", user.PhoneNumber);
        Assert.True(user.IsPhoneVerified);
    }

    [Fact]
    public void UpdateProfile_StillSetsAnUnverifiedContactNumber()
    {
        var user = User.CreateWithEmail("owner@test.com", "hash", "Real", "Owner");

        user.UpdateProfile("owner@test.com", "+919000000000", "Real", "Owner", "en-IN");

        Assert.Equal("+919000000000", user.PhoneNumber);
        Assert.False(user.IsPhoneVerified);
    }

    [Fact]
    public void UpdateShopUserProfile_CannotMoveAVerifiedPhone()
    {
        var user = WithVerifiedPhone();

        user.UpdateShopUserProfile("staff@test.com", "Staff", "Member", "+919000000000");

        Assert.Equal("+919876543210", user.PhoneNumber);
    }
}
