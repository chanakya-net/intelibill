using Intelibill.Domain.Common;
using Intelibill.Domain.Events;

namespace Intelibill.Domain.Entities;

public sealed class User : BaseEntity
{
    private const string DefaultLanguage = "en-IN";
    private static readonly HashSet<string> SupportedLanguages =
    [
        "en-IN",
        "hi-IN",
        "ta-IN",
        "te-IN",
        "bn-IN",
        "ml-IN",
    ];

    public string? Email { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? PasswordHash { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Language { get; private set; } = DefaultLanguage;
    public bool IsEmailVerified { get; private set; }
    public bool IsLoginEnabled { get; private set; } = true;

    private readonly List<UserExternalLogin> _externalLogins = [];
    public IReadOnlyList<UserExternalLogin> ExternalLogins => _externalLogins.AsReadOnly();

    private readonly List<ShopMembership> _shopMemberships = [];
    public IReadOnlyList<ShopMembership> ShopMemberships => _shopMemberships.AsReadOnly();

    private User() { }

    public static User CreateWithEmail(
        string email,
        string passwordHash,
        string firstName,
        string lastName,
        string? phoneNumber = null)
    {
        var user = new User
        {
            Email = email.ToLowerInvariant(),
            PasswordHash = passwordHash,
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim(),
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

    public void SetLoginEnabled(bool isLoginEnabled)
    {
        IsLoginEnabled = isLoginEnabled;
    }

    public void UpdateProfile(string email, string? phoneNumber, string firstName, string lastName, string? language)
    {
        Email = email.Trim().ToLowerInvariant();
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        SetLanguage(language);
    }

    public void SetLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            Language = DefaultLanguage;
            return;
        }

        var normalizedLanguage = language.Trim();
        Language = SupportedLanguages.Contains(normalizedLanguage)
            ? normalizedLanguage
            : DefaultLanguage;
    }

    public void UpdateShopUserProfile(string email, string firstName, string lastName, string phoneNumber)
    {
        Email = email.Trim().ToLowerInvariant();
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        PhoneNumber = phoneNumber.Trim();
    }
}
