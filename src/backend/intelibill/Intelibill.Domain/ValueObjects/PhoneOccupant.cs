namespace Intelibill.Domain.ValueObjects;

/// <summary>
/// Who currently holds a phone number, read while that account's row is locked. A number can be
/// occupied in two very different ways: as a proved sign-in factor, or as contact information
/// somebody typed into a profile. Only the first is authority over the account.
/// </summary>
public sealed record PhoneOccupant(
    Guid UserId,
    long SecurityVersion,
    bool IsLoginEnabled,
    bool IsPhoneVerified,
    bool HasEmail,
    bool IsEmailVerified,
    bool RequiresOperatorRecovery)
{
    /// <summary>
    /// True when proving the number again should return the holder to their own account: they
    /// bound it as a credential themselves.
    /// </summary>
    public bool IsProvedCredential => IsPhoneVerified && IsLoginEnabled;

    /// <summary>
    /// True when a claim on this number must go to an audited support process instead of an
    /// automatic one. Carriers recycle numbers, so proving the number today says nothing about
    /// having owned the account that accumulated this business history — and the number was never
    /// proved by its holder in the first place.
    /// </summary>
    public bool RequiresOwnershipReview => !IsPhoneVerified && RequiresOperatorRecovery;
}
