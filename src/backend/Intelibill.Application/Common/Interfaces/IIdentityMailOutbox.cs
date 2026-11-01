namespace Intelibill.Application.Common.Interfaces;

public interface IIdentityMailOutbox
{
    bool Enabled { get; }
    void QueueChallenge(Guid id, string destination, string purpose, string token, DateTimeOffset expiresAt);

    /// <summary>
    /// Queues a shop invitation. The single-use token travels only in this message, so holding the
    /// link is what proves the invited address was reachable.
    /// </summary>
    void QueueShopInvitation(Guid invitationId, string destination, string shopName, string role, string token, DateTimeOffset expiresAt);

    /// <summary>
    /// Queues a password-reset link in the caller's transaction, so the proof and the only copy of
    /// its link commit or roll back together. Delivery is retried until the token expires, is used
    /// or is superseded by a newer request.
    /// </summary>
    void QueuePasswordReset(Guid passwordResetTokenId, string destination, string token, DateTimeOffset expiresAt);

    void QueueSecurityNotice(string destination);

    /// <summary>Non-bearer notice to the verified address that the account password was changed.</summary>
    void QueuePasswordChangedNotice(string destination);
}
