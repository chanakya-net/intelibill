using Intelibill.Domain.Entities;
namespace Intelibill.Domain.Interfaces.Repositories;

public interface IEmailOwnershipRepository
{
    Task<IAsyncDisposable> BeginAsync(CancellationToken ct);
    Task<EmailOwnershipChallenge?> FindAsync(Guid id, CancellationToken ct);
    Task SupersedeAsync(string destination, string purpose, Guid? userId, DateTimeOffset now, CancellationToken ct);
    Task<bool> HasSendBudgetAsync(string destination, DateTimeOffset now, CancellationToken ct);
    Task<bool> HasBusinessHistoryAsync(Guid userId, CancellationToken ct);
    Task InvalidateCredentialsAsync(Guid userId, CancellationToken ct);
    void Add(EmailOwnershipChallenge challenge);
    void AddMail(IdentityMail mail);
    void AddExternalLogin(UserExternalLogin login);
    Task<bool> CommitAsync(CancellationToken ct);
}
