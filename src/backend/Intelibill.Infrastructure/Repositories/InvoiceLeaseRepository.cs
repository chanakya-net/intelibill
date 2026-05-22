using System.Data;
using Intelibill.Domain.Common;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class InvoiceLeaseRepository : RepositoryBase<InvoiceLease>, IInvoiceLeaseRepository
{
    private readonly ApplicationDbContext _context;
    private const int MaxReservationAttempts = 3;

    public InvoiceLeaseRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public IAsyncEnumerable<InvoiceLease> StreamActiveByShopAsync(
        Guid shopId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        DbSet
            .AsNoTracking()
            .Where(l => l.ShopId == shopId && l.ExpiresAt > now)
            .OrderBy(l => l.ExpiresAt)
            .AsAsyncEnumerable();

    public async Task<IReadOnlyList<InvoiceLease>> GetActiveByDeviceAsync(
        Guid shopId,
        string deviceId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .Where(l => l.ShopId == shopId
                && l.DeviceId == deviceId
                && l.ExpiresAt > now)
            .OrderBy(l => l.ExpiresAt)
            .ToListAsync(cancellationToken);

    public async Task<InvoiceLease> ReserveAsync(
        Guid shopId,
        string deviceId,
        int fiscalYearStart,
        string prefix,
        int blockSize,
        DateTimeOffset reservedAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxReservationAttempts; attempt++)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

                var sequenceId = Guid.NewGuid();
                await _context.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO invoice_sequences (id, shop_id, fiscal_year_start, next_number, prefix, created_at)
                    VALUES ({0}, {1}, {2}, {3}, {4}, {5})
                    ON CONFLICT (shop_id, fiscal_year_start) DO NOTHING;
                    """,
                    sequenceId,
                    shopId,
                    fiscalYearStart,
                    1,
                    prefix,
                    reservedAt);

                var sequence = await _context.InvoiceSequences
                    .FromSqlRaw(
                        "SELECT * FROM invoice_sequences WHERE shop_id = {0} AND fiscal_year_start = {1} FOR UPDATE",
                        shopId,
                        fiscalYearStart)
                    .SingleAsync(cancellationToken);

                var (start, end) = sequence.ReserveBlock(blockSize);
                var lease = InvoiceLease.Create(
                    shopId,
                    sequence.Id,
                    deviceId,
                    fiscalYearStart,
                    sequence.Prefix,
                    start,
                    end,
                    InvoiceLeaseDefaults.NumberPadding,
                    reservedAt,
                    expiresAt);

                await DbSet.AddAsync(lease, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return lease;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure && attempt < MaxReservationAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("Invoice lease reservation failed after retries.");
    }
}
