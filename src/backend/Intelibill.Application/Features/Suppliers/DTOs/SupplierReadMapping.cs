using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Suppliers.DTOs;

internal static class SupplierReadMapping
{
    internal static async Task<IReadOnlyList<SupplierDto>> EnrichAsync(Guid shopId, IReadOnlyList<Supplier> suppliers,
        ISupplierLedgerEntryRepository ledger, CancellationToken ct)
    {
        var balances = await ledger.GetSupplierBalancesAsync(shopId, suppliers.Select(s => s.Id).ToArray(), ct);
        return suppliers.Select(s => new SupplierDto(s.Id, s.IsSystem ? "Unassigned" : s.Name,
            s.ContactPersonName, s.ContactPersonPhone, s.Address, s.City, s.State, s.Pin,
            s.IsSystem, s.IsActive, s.IsPreferred, balances.GetValueOrDefault(s.Id))).ToList();
    }
}
