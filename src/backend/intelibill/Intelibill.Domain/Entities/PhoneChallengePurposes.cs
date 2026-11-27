namespace Intelibill.Domain.Entities;

/// <summary>
/// What a challenge may be spent on. A challenge is bound to one of these and is refused anywhere
/// else, so a code delivered to verify a new number can never be presented as a sign-in.
/// </summary>
public static class PhoneChallengePurposes
{
    public const string Enrollment = "enrollment";
    public const string Login = "login";
    public const string PhoneChange = "phone-change";
}
