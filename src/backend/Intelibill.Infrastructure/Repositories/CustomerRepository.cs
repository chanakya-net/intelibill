using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Repositories;

internal sealed class CustomerRepository(ApplicationDbContext context)
    : RepositoryBase<Customer>(context), ICustomerRepository
{
    public async Task<IReadOnlyList<Customer>> GetByOwnerUserIdAsync(Guid ownerUserId, CancellationToken cancellationToken = default) =>
        await DbSet
            .Where(c => c.OwnerUserId == ownerUserId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

    public async Task<Customer?> GetByOwnerAndIdAsync(Guid ownerUserId, Guid customerId, CancellationToken cancellationToken = default) =>
        await DbSet
            .FirstOrDefaultAsync(c => c.OwnerUserId == ownerUserId && c.Id == customerId, cancellationToken);

    public async Task<Customer?> GetByOwnerAndPhoneAsync(Guid ownerUserId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var normalizedPhoneNumber = phoneNumber.Trim();

        return await DbSet
            .FirstOrDefaultAsync(c => c.OwnerUserId == ownerUserId && c.PhoneNumber == normalizedPhoneNumber, cancellationToken);
    }
}
