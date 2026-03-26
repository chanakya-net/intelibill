using Intelibill.Domain.Common;

namespace Intelibill.Domain.Entities;

public sealed class PasswordResetToken : BaseEntity
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public bool IsUsed { get; private set; }

    public User User { get; private set; } = null!;

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    public bool IsValid => !IsUsed && !IsExpired;

    private PasswordResetToken() { }

    public static PasswordResetToken Create(Guid userId, string tokenHash, int expiryHours = 2)
    {
        return new PasswordResetToken
        {
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(expiryHours),
        };
    }

    public void MarkAsUsed()
    {
        IsUsed = true;
    }
}
