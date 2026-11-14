using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.AdminIdentities;

public interface IAdminInvitationStore
{
    /// <summary>The provider origin when <paramref name="adminId"/> is the persisted, MFA-enrolled platform owner.</summary>
    Task<string?> FindOwnerOriginAsync(Guid adminId, CancellationToken cancellationToken);

    /// <summary>The provider origin once the first owner exists; invitations are unavailable before that.</summary>
    Task<string?> FindProviderOriginAsync(CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="operation"/> in one transaction and saves whatever it staged, refusals included.</summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    /// <summary>Serializes every identity change for one address until the transaction ends.</summary>
    Task LockEmailAsync(string email, CancellationToken cancellationToken);

    Task<bool> AdminExistsAsync(string email, CancellationToken cancellationToken);

    /// <summary>Retires the address's open invitations and returns their identifiers.</summary>
    Task<IReadOnlyList<Guid>> SupersedeOpenAsync(string email, DateTimeOffset now, CancellationToken cancellationToken);

    Task<string?> FindInvitedEmailAsync(string digest, CancellationToken cancellationToken);

    Task<AdminInvitation?> LockInvitationAsync(string digest, CancellationToken cancellationToken);

    /// <summary>Retires an invitation whose message could not be handed to the mail transport.</summary>
    Task RetireUndeliveredAsync(Guid invitationId, Guid actorId, DateTimeOffset now, CancellationToken cancellationToken);

    void Add(AdminInvitation invitation);
    void Add(AdminIdentity identity);
    void Add(BootstrapEnrollmentCredential credential);
    void Add(ProviderAuditEvent auditEvent);
}

public sealed record AdminInvitationMessage(string Recipient, Uri Link, DateTimeOffset ExpiresAt);

public interface IAdminInvitationMailer
{
    /// <summary>False when the message was not accepted by the configured transport.</summary>
    Task<bool> TrySendAsync(AdminInvitationMessage message, CancellationToken cancellationToken);
}
