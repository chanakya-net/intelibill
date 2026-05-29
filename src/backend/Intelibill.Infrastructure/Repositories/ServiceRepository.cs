using System.Data;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ServiceRepository(ApplicationDbContext context)
    : RepositoryBase<Service>(context), IServiceRepository
{
    private readonly ApplicationDbContext _context = context;
    private const int MaxCodeGenerationAttempts = 3;

    public async Task<Service?> GetByCodeAsync(Guid shopId, string code, CancellationToken cancellationToken = default)
    {
        var normalizedCode = code.Trim();
        return await DbSet
            .FirstOrDefaultAsync(s => s.ShopId == shopId && s.Code == normalizedCode, cancellationToken);
    }

    public async Task<Service?> GetByNameAsync(Guid shopId, string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = name.Trim();
        return await DbSet
            .FirstOrDefaultAsync(s => s.ShopId == shopId && s.Name == normalizedName, cancellationToken);
    }

    public async Task<string> GetNextCodeAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxCodeGenerationAttempts; attempt++)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

                await _context.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO service_code_sequences (id, shop_id, next_number, prefix, created_at)
                    VALUES ({0}, {1}, {2}, {3}, {4})
                    ON CONFLICT (shop_id) DO NOTHING;
                    """,
                    Guid.NewGuid(),
                    shopId,
                    1,
                    ServiceCodeSequence.CodePrefix,
                    DateTimeOffset.UtcNow);

                var sequence = await _context.ServiceCodeSequences
                    .FromSqlRaw("SELECT * FROM service_code_sequences WHERE shop_id = {0} FOR UPDATE", shopId)
                    .SingleAsync(cancellationToken);

                var code = sequence.NextCode();

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return code;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure && attempt < MaxCodeGenerationAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("Service code generation failed after retries.");
    }

    public async Task<IReadOnlyList<Service>> GetByShopIdAsync(
        Guid shopId,
        bool includeInactive,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedSearch = search?.Trim();

        var query = _context.Services
            .Where(s => s.ShopId == shopId);

        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var pattern = $"%{normalizedSearch}%";
            query = query.Where(s =>
                EF.Functions.ILike(s.Name, pattern)
                || EF.Functions.ILike(s.Code, pattern)
                || (s.Description != null && EF.Functions.ILike(s.Description, pattern))
                || (s.HsnCode != null && EF.Functions.ILike(s.HsnCode, pattern)));
        }

        return await query
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }
}
