using Intelibill.Licensing.Domain.Entities;

namespace Intelibill.Licensing.Application.Bootstrap;

public interface IBootstrapStore
{
    Task<InitialAdminBootstrap> ReadAsync(CancellationToken cancellationToken);
    Task<T> WithLockedBootstrapAsync<T>(
        Func<InitialAdminBootstrap, CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
    Task<AdminIdentity?> LockPendingOwnerAsync(Guid id, CancellationToken cancellationToken);
    Task<AdminIdentity?> FindPendingOwnerAsync(Guid id, CancellationToken cancellationToken);
    Task<EnrollmentState?> FindEnrollmentAsync(string digest, DateTimeOffset now, CancellationToken cancellationToken);
    Task RevokeEnrollmentAsync(Guid ownerId, DateTimeOffset now, CancellationToken cancellationToken);
    void Add(AdminIdentity identity);
    void Add(BootstrapEnrollmentCredential credential);
    void Add(ProviderAuditEvent auditEvent);
}

public sealed record EnrollmentState(Guid OwnerId, DateTimeOffset ExpiresAt);

public sealed class BootstrapSecret(string value, string digest)
{
    public string Value { get; } = value;
    public string Digest { get; } = digest;
}

public interface IBootstrapSecrets
{
    BootstrapSecret CreateProof();
    BootstrapSecret CreateEnrollment();
    BootstrapSecret CreateInvitation();
    bool MatchesProof(string value, string digest);
    string HashEnrollment(string credential);
    string HashInvitation(string token);
    string HashPassword(string password);
    bool VerifyPassword(string password, string hash);
}

public interface IBootstrapRateBudget
{
    // Return the UTC reset time when the attempt exceeds a budget.
    Task<DateTimeOffset?> TakeAsync(bool isResume, DateTimeOffset now, CancellationToken cancellationToken);

    // Durable budgets for invitation issuance and acceptance, shared by every replica.
    Task<DateTimeOffset?> TakeInvitationAsync(bool isAcceptance, DateTimeOffset now, CancellationToken cancellationToken);
}
