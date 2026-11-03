using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class InvoiceLeaseRepository : RepositoryBase<InvoiceLease>, IInvoiceLeaseRepository
{
    public InvoiceLeaseRepository(ApplicationDbContext context) : base(context)
    {
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

    public async Task<InvoiceLease?> FindReservedRangeAsync(
        Guid shopId,
        string deviceId,
        string invoiceNumber,
        CancellationToken cancellationToken = default)
    {
        var leases = await DbSet
            .AsNoTracking()
            .Where(l => l.ShopId == shopId && l.DeviceId == deviceId && invoiceNumber.StartsWith(l.Prefix))
            .OrderBy(l => l.ReservedAt)
            .ToListAsync(cancellationToken);

        return leases.FirstOrDefault(l =>
            int.TryParse(invoiceNumber[l.Prefix.Length..], out var number)
            && number >= l.RangeStart
            && number <= l.RangeEnd);
    }
}
