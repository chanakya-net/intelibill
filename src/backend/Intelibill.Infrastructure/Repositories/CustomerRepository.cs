using Intelibill.Domain.Entities;
using Intelibill.Domain.ReadModels;
using Intelibill.Domain.ValueObjects;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class CustomerRepository(ApplicationDbContext context)
    : RepositoryBase<Customer>(context), ICustomerRepository
{

    public async Task<PartyDirectoryPage<Customer>> GetDirectoryAsync(Guid shopId, PartyDirectoryQuery query, CancellationToken cancellationToken = default)
    {
        var parties = DbSet.AsNoTracking().Where(p => p.ShopId == shopId);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            var pattern = PartyDirectorySearch.Pattern(search);
            parties = parties.Where(p => EF.Functions.ILike(p.Name, pattern, "\\")
                || EF.Functions.ILike(p.PhoneNumber, pattern, "\\")
                || EF.Functions.ILike((p.Address ?? ""), pattern, "\\")
                || EF.Functions.ILike(p.Id.ToString(), pattern, "\\"));
        }
        var status = query.Status.Trim().ToLowerInvariant();
        if (status != "all") parties = parties.Where(p => p.IsActive == (status == "active"));
        var count = await parties.CountAsync(cancellationToken);
        var items = await parties.OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip(checked((query.PageNumber - 1) * query.PageSize)).Take(query.PageSize).ToListAsync(cancellationToken);
        return new(items, count, query.PageNumber, query.PageSize);
    }

    public async Task<CustomerDirectorySummary> GetDirectorySummaryAsync(Guid shopId, UtcDateWindow month, CancellationToken cancellationToken = default)
    {
        var parties = DbSet.AsNoTracking().Where(p => p.ShopId == shopId);
        var summary = await parties.GroupBy(_ => 1)
            .Select(g => new { Total = g.Count(), Credit = g.Sum(p => p.CreditLimit), WithCredit = g.Count(p => p.CreditLimit > 0m) })
            .SingleOrDefaultAsync(cancellationToken);
        var balances = await PartyLedgerQueries.Customers(Context.Set<CustomerLedgerEntry>()
                .Where(e => e.ShopId == shopId && parties.Any(p => p.Id == e.CustomerId)))
            .GroupBy(_ => 1)
            .Select(g => new { Outstanding = g.Sum(b => b.Amount > 0m ? b.Amount : 0m),
                OutstandingCount = g.Count(b => b.Amount > 0m), Unknown = g.Sum(b => b.UnknownTypes) })
            .SingleOrDefaultAsync(cancellationToken);
        PartyLedgerQueries.RejectUnknown(balances?.Unknown ?? 0);
        var sales = Context.Set<Sale>().Where(s => s.ShopId == shopId && s.CustomerId != null
            && DbSet.Any(p => p.ShopId == shopId && p.Id == s.CustomerId));
        var revenue = await sales.Where(s => s.SoldAt >= month.StartInclusive && s.SoldAt < month.EndExclusive)
            .SumAsync(s => s.TotalAmount, cancellationToken);
        var cancelled = await (from c in Context.Set<SaleCancellation>()
            join s in sales on new { c.ShopId, c.SaleId } equals new { s.ShopId, SaleId = s.Id }
            where c.ShopId == shopId && c.CancelledAt >= month.StartInclusive && c.CancelledAt < month.EndExclusive
            select c.TotalAmount).SumAsync(cancellationToken);
        return new(summary?.Total ?? 0, balances?.Outstanding ?? 0m, balances?.OutstandingCount ?? 0,
            summary?.Credit ?? 0m, summary?.WithCredit ?? 0, revenue - cancelled);
    }

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
