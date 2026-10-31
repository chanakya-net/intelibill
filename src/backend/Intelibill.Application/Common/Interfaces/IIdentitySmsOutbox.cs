namespace Intelibill.Application.Common.Interfaces;

/// <summary>
/// Where a verification code goes. It is written encrypted into the caller's own transaction, so a
/// code only exists for a challenge that actually committed, and a worker delivers it afterwards
/// without holding a database transaction across the network call.
/// </summary>
public interface IIdentitySmsOutbox
{
    /// <summary>False when no transport is configured. Nothing should promise a code it cannot send.</summary>
    bool Enabled { get; }

    void QueueCode(Guid challengeId, string destination, string purpose, string code, DateTimeOffset expiresAt);

    /// <summary>Tells a number that it stopped, or started, being a sign-in factor on an account.</summary>
    void QueueSecurityNotice(string destination, bool bound);
}
