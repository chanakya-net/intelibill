using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class CustomerRepository(ApplicationDbContext context)
    : RepositoryBase<Customer>(context), ICustomerRepository
{
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
}
