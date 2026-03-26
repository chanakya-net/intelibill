using InventoryAI.Domain.Common;
using InventoryAI.Domain.Enums;

namespace InventoryAI.Domain.Entities;

public sealed class UserExternalLogin : BaseEntity
{
    public Guid UserId { get; private set; }
    public ExternalAuthProvider Provider { get; private set; }
    public string ProviderKey { get; private set; } = string.Empty;
    public string? ProviderEmail { get; private set; }

    public User User { get; private set; } = null!;

    private UserExternalLogin() { }

    public static UserExternalLogin Create(
        Guid userId,
        ExternalAuthProvider provider,
        string providerKey,
        string? providerEmail)
    {
        return new UserExternalLogin
        {
            UserId = userId,
            Provider = provider,
            ProviderKey = providerKey,
            ProviderEmail = providerEmail,
        };
    }
}
