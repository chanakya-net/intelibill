using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class CustomerRepository(ApplicationDbContext context)
    : RepositoryBase<Customer>(context), ICustomerRepository
{
    public IAsyncEnumerable<Customer> StreamActiveByShopAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        DbSet
            .AsNoTracking()
            .Where(c => c.ShopId == shopId && c.IsActive)
            .OrderBy(c => c.Name)
            .AsAsyncEnumerable();

    public async Task<IReadOnlyList<Customer>> GetByShopIdAsync(Guid shopId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(c => c.ShopId == shopId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

    public async Task<Customer?> GetByShopAndIdAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default) =>
        await DbSet
            .FirstOrDefaultAsync(c => c.ShopId == shopId && c.Id == customerId, cancellationToken);

    public async Task<Customer?> GetByShopAndPhoneAsync(Guid shopId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var normalizedPhoneNumber = phoneNumber.Trim();

        return await DbSet
            .FirstOrDefaultAsync(c => c.ShopId == shopId && c.PhoneNumber == normalizedPhoneNumber, cancellationToken);
    }

    private const string CreditLockTimeout = "5s";

    public async Task<RowLockOutcome> LockForCreditAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default)
    {
        if (!Context.Database.IsNpgsql())
        {
            return await DbSet.AnyAsync(c => c.Id == customerId && c.ShopId == shopId, cancellationToken)
                ? RowLockOutcome.Acquired
                : RowLockOutcome.NotFound;
        }

        if (Context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The customer credit lock must be taken inside a transaction.");

        await Context.Database.ExecuteSqlAsync(
            $"SELECT set_config('lock_timeout', {CreditLockTimeout}, true)",
            cancellationToken);

        List<Guid> locked;
        try
        {
            // NO KEY UPDATE, as payments and returns take it: credit decisions, payments and returns
            // on one account exclude one another, and a customer edit (an UPDATE) waits for them.
            locked = await Context.Database
                .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM customers WHERE id = {customerId} AND shop_id = {shopId} FOR NO KEY UPDATE")
                .ToListAsync(cancellationToken);
        }
        // Npgsql counts a lock timeout as transient, so the execution strategy hands it back wrapped.
        catch (Exception ex) when ((ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            // The statement failed, so the transaction is now aborted; the caller rolls it back.
            return RowLockOutcome.TimedOut;
        }

        // The budget is for this lock only: the invoice series, credit notes and batches locked
        // later in the posting keep the default wait they had before.
        await Context.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout TO DEFAULT", cancellationToken);

        return locked.Count == 0 ? RowLockOutcome.NotFound : RowLockOutcome.Acquired;
    }

    public async Task<Customer?> GetCurrentAsync(Guid shopId, Guid customerId, CancellationToken cancellationToken = default) =>
        await DbSet
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ShopId == shopId && c.Id == customerId, cancellationToken);
}
