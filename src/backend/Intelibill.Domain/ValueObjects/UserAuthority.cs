namespace Intelibill.Domain.ValueObjects;

/// <summary>
/// The account state a session may be started or continued under, read while the account row is
/// locked so nothing can move between the read and the decision that follows it.
/// </summary>
public sealed record UserAuthority(
    Guid UserId,
    long SecurityVersion,
    bool IsLoginEnabled,
    bool HasEmail,
    bool IsEmailVerified)
{
    /// <summary>
    /// An account may hold a session when login is enabled and, where the account signs in with an
    /// email address, that address has been proved.
    /// </summary>
    public bool CanHoldSession => IsLoginEnabled && (!HasEmail || IsEmailVerified);
}
