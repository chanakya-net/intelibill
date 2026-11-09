using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Suppliers.Queries.GetSupplierDirectory;

public sealed class GetSupplierDirectoryQueryHandler(IUserRepository users, ISupplierRepository suppliers, ISupplierLedgerEntryRepository ledger)
{
    public async Task<ErrorOr<SupplierDirectoryDto>> HandleAsync(GetSupplierDirectoryQuery query, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (user is null) return Errors.Auth.UserNotFound;
        if (!user.ShopMemberships.Any(m => m.ShopId == query.ShopId && m.IsActive)) return Errors.Shop.MembershipNotFound;
        var page = await suppliers.GetDirectoryAsync(query.ShopId, query.Filter, query.IncludeSystem, cancellationToken);
        var items = await SupplierReadMapping.EnrichAsync(query.ShopId, page.Items, ledger, cancellationToken);
        var summary = await suppliers.GetDirectorySummaryAsync(query.ShopId, query.IncludeSystem, cancellationToken);
        return new SupplierDirectoryDto(items, page.TotalCount, page.PageNumber, page.PageSize, summary);
    }
}
