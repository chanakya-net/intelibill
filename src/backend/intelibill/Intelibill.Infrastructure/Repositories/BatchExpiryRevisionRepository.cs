using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
namespace Intelibill.Infrastructure.Repositories;

public sealed class BatchExpiryRevisionRepository(ApplicationDbContext db) : IBatchExpiryRevisionRepository
{
    public async Task AddAsync(BatchExpiryRevision revision, CancellationToken ct) => await db.Set<BatchExpiryRevision>().AddAsync(revision, ct);
}
