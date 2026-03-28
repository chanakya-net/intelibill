using Intelibill.Domain.Common;
using Intelibill.Domain.Events;

namespace Intelibill.Domain.Entities;

public sealed class User : BaseEntity
{
    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? PasswordHash { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public bool IsEmailVerified { get; private set; }

    private readonly List<UserExternalLogin> _externalLogins = [];
    public IReadOnlyList<UserExternalLogin> ExternalLogins => _externalLogins.AsReadOnly();

    private readonly List<ShopMembership> _shopMemberships = [];
    public IReadOnlyList<ShopMembership> ShopMemberships => _shopMemberships.AsReadOnly();

    private User() { }

    public static User CreateWithEmail(string email, string passwordHash, string firstName, string lastName)
    {
        var user = new User
        {
            Email = email.ToLowerInvariant(),
            PasswordHash = passwordHash,
            FirstName = firstName,
            LastName = lastName,
        };
        user.AddDomainEvent(new UserRegisteredEvent(user.Id, user.Email));
        return user;
    }

    public static User CreateWithPhone(string phoneNumber, string firstName, string lastName)
    {
        var user = new User
        {
            PhoneNumber = phoneNumber,
            FirstName = firstName,
            LastName = lastName,
        };
        user.AddDomainEvent(new UserRegisteredEvent(user.Id, null));
        return user;
    }

    public static User CreateFromExternalProvider(string? email, string firstName, string lastName)
    {
        var user = new User
        {
            Email = email?.ToLowerInvariant(),
            FirstName = firstName,
            LastName = lastName,
            IsEmailVerified = email is not null,
        };
        user.AddDomainEvent(new UserRegisteredEvent(user.Id, user.Email));
        return user;
    }

    public void AddExternalLogin(UserExternalLogin login) => _externalLogins.Add(login);

    public void AddShopMembership(ShopMembership membership)
    {
        membership.AttachUser(this);
        _shopMemberships.Add(membership);
    }

    public void UpdatePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
    }

    public void UpdateProfile(string email, string? phoneNumber, string firstName, string lastName)
    {
        Email = email.Trim().ToLowerInvariant();
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
    }
}
