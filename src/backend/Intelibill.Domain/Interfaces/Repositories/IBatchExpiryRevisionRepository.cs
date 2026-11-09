using Intelibill.Domain.Entities;
namespace Intelibill.Domain.Interfaces.Repositories;

public interface IBatchExpiryRevisionRepository
{
    Task AddAsync(BatchExpiryRevision revision, CancellationToken ct);
}
