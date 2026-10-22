namespace Intelibill.Application.Common.Interfaces;

public interface IIdentityMailOutbox
{
    bool Enabled { get; }
    void QueueChallenge(Guid id, string destination, string purpose, string token, DateTimeOffset expiresAt);
    void QueueSecurityNotice(string destination);
}
