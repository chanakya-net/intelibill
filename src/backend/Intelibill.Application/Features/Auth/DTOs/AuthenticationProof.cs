namespace Intelibill.Application.Features.Auth.DTOs;

/// <summary>
/// The record of a credential check that just succeeded. A proof is built on the successful side of
/// a password, enrollment or registration check and handed straight to session creation; nothing a
/// client sends can become one, because no request model reaches this type.
/// </summary>
/// <param name="VerifiedSecurityVersion">
/// The account version the credential was actually checked against. Session creation re-reads the
/// account under its lock and refuses a proof whose version has moved, so a password verified a
/// moment before a reset or a disablement cannot be relabelled as current authority.
/// </param>
public sealed record AuthenticationProof(
    Guid UserId,
    CredentialKind Credential,
    long VerifiedSecurityVersion,
    DateTimeOffset VerifiedAt,
    Guid? ChallengeId = null)
{
    public static AuthenticationProof FromPassword(Guid userId, long verifiedSecurityVersion, DateTimeOffset verifiedAt) =>
        new(userId, CredentialKind.Password, verifiedSecurityVersion, verifiedAt);

    /// <summary>A single-use mailbox proof, bound to the challenge that authorised it.</summary>
    public static AuthenticationProof FromEmailOwnership(Guid userId, long verifiedSecurityVersion, Guid challengeId, DateTimeOffset verifiedAt) =>
        new(userId, CredentialKind.EmailOwnership, verifiedSecurityVersion, verifiedAt, challengeId);

    /// <summary>
    /// A just-created account, which has no prior credential to check. Valid only in the same
    /// transaction that inserts the account.
    /// </summary>
    public static AuthenticationProof FromRegistration(Guid userId, DateTimeOffset verifiedAt) =>
        new(userId, CredentialKind.Registration, 0, verifiedAt);
}

public enum CredentialKind
{
    Password = 1,
    EmailOwnership = 2,
    Registration = 3,
}
