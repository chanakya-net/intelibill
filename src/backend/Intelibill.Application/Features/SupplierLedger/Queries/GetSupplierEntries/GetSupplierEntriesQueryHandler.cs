using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.SupplierLedger.DTOs;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.SupplierLedger.Queries.GetSupplierEntries;

public sealed class GetSupplierEntriesQueryHandler(
    IUserRepository userRepository,
    ISupplierRepository supplierRepository,
    ISupplierLedgerEntryRepository ledgerRepository,
    ISupplierReturnRepository? supplierReturns = null)
{
    public async Task<ErrorOr<IReadOnlyList<SupplierLedgerEntryDto>>> HandleAsync(GetSupplierEntriesQuery query, CancellationToken cancellationToken)
    {
        var caller = await userRepository.GetByIdWithDetailsAsync(query.UserId, cancellationToken);
        if (caller is null)
            return Errors.Auth.UserNotFound;

        var callerMembership = caller.ShopMemberships.FirstOrDefault(sm => sm.ShopId == query.ActiveShopId);
        if (callerMembership is null)
            return Errors.Shop.MembershipNotFound;

        var supplier = await supplierRepository.GetByIdAsync(query.SupplierId, cancellationToken);
        if (supplier is null || supplier.ShopId != query.ActiveShopId)
            return Errors.Supplier.SupplierNotFound;

        var entries = await ledgerRepository.GetBySupplierAsync(query.ActiveShopId, query.SupplierId, cancellationToken);

        var documents=supplierReturns is null?[]:await supplierReturns.GetByLedgerEntriesAsync(query.ActiveShopId,entries.Select(x=>x.Id).ToArray(),cancellationToken);
        var links=documents.SelectMany(x=>x.Credit!.Lines.Where(l=>l.SupplierLedgerEntryId.HasValue).Select(l=>new {LedgerId=l.SupplierLedgerEntryId!.Value,ReturnId=x.Id})).ToDictionary(x=>x.LedgerId,x=>(Guid?)x.ReturnId);

        return entries
            .Select(e => new SupplierLedgerEntryDto(
                e.Id,
                e.SupplierId,
                e.EntryType,
                e.Amount,
                e.EntryDate,
                e.Notes,
                SupplierReturnId:links.GetValueOrDefault(e.Id)))
            .ToList();
    }
}
