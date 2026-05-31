using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Npgsql;
using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class ItemBarcodeSequenceRepository(ApplicationDbContext context) : IItemBarcodeSequenceRepository
{
    private readonly ApplicationDbContext _context = context;
    private const int MaxCodeGenerationAttempts = 10;

    public async Task<string> GetNextCodeAsync(Guid shopId, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= MaxCodeGenerationAttempts; attempt++)
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

                await _context.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO item_barcode_sequences (id, shop_id, next_number, prefix, created_at)
                    VALUES ({0}, {1}, {2}, {3}, {4})
                    ON CONFLICT (shop_id) DO NOTHING;
                    """,
                    Guid.NewGuid(),
                    shopId,
                    1,
                    ItemBarcodeSequence.CodePrefix,
                    DateTimeOffset.UtcNow);

                var sequence = await _context.ItemBarcodeSequences
                    .FromSqlRaw("SELECT * FROM item_barcode_sequences WHERE shop_id = {0} FOR UPDATE", shopId)
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
            catch (DbUpdateException ex) when (IsRetryable(ex) && attempt < MaxCodeGenerationAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
            catch (InvalidOperationException ex) when (IsRetryable(ex) && attempt < MaxCodeGenerationAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < MaxCodeGenerationAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt), cancellationToken);
            }
        }

        throw new InvalidOperationException("Item barcode generation failed after retries.");
    }

    private static bool IsRetryable(Exception exception)
    {
        var current = exception;
        while (current is not null)
        {
            if (current is PostgresException postgresException && postgresException.SqlState == PostgresErrorCodes.SerializationFailure)
            {
                return true;
            }

            if (current is NpgsqlException npgsqlException
                && npgsqlException.Message.Contains("could not serialize access due to concurrent update", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (current is TimeoutException)
            {
                return true;
            }

            if (current is DbUpdateException dbUpdateException &&
                dbUpdateException.InnerException is OperationCanceledException)
            {
                return false;
            }

            current = current.InnerException;
        }

        return false;
    }
}
