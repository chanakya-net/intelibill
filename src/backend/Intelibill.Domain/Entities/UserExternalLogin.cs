using ErrorOr;
using Intelibill.Domain.Common;
using Intelibill.Domain.Enums;

namespace Intelibill.Domain.Entities;

public sealed class UserExternalLogin : BaseEntity
{
    public Guid UserId { get; private set; }
    public ExternalAuthProvider Provider { get; private set; }
    public string ProviderKey { get; private set; } = string.Empty;
    public string? ProviderEmail { get; private set; }

    public User User { get; private set; } = null!;

    private UserExternalLogin() { }

    public static ErrorOr<UserExternalLogin> Create(
        Guid userId,
        ExternalAuthProvider provider,
        string providerKey,
        string? providerEmail)
    {
        if (string.IsNullOrWhiteSpace(providerKey))
        {
            return Error.Validation("UserExternalLogin.ProviderKeyRequired", "Provider key is required.");
        }

        return new UserExternalLogin
        {
            UserId = userId,
            Provider = provider,
            ProviderKey = providerKey.Trim(),
            ProviderEmail = providerEmail,
        };
    }
}
