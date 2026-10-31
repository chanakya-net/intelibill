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

    void QueueSecurityNotice(string destination);
}
