using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Payments.DTOs;
using Intelibill.Application.Features.Payments.Services;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Payments.Queries.GetPartyBalances;

/// <summary>Open documents and unallocated credit of one customer or supplier.</summary>
public sealed class GetPartyBalancesQueryHandler(
    IUserRepository userRepository,
    ICustomerRepository customerRepository,
    ISupplierRepository supplierRepository,
    IInvoiceBalanceReader balanceReader)
{
    public async Task<ErrorOr<PartyBalancesDto>> HandleAsync(GetPartyBalancesQuery query, CancellationToken cancellationToken)
    {
        var authorized = await PaymentAuthorization.RequireOwnerOrManagerAsync(
            userRepository, query.ActorUserId, query.ActiveShopId, cancellationToken);
        if (authorized.IsError)
            return authorized.Errors;

        if (!Enum.IsDefined(query.PartyType))
            return Errors.Payment.PartyTypeInvalid;

        var exists = query.PartyType == PaymentPartyType.Customer
            ? await customerRepository.GetByShopAndIdAsync(query.ActiveShopId, query.PartyId, cancellationToken) is not null
            : await supplierRepository.GetByIdAsync(query.PartyId, cancellationToken) is { } supplier && supplier.ShopId == query.ActiveShopId;
        if (!exists)
            return Errors.Payment.PartyNotFound;

        var receipts = await balanceReader.GetReceiptsAsync(query.ActiveShopId, query.PartyType, query.PartyId, cancellationToken);
        IReadOnlyList<CustomerInvoiceBalanceDto> invoices = query.PartyType == PaymentPartyType.Customer
            ? await balanceReader.GetForCustomerAsync(query.ActiveShopId, query.PartyId, null, cancellationToken)
            : [];
        IReadOnlyList<SupplierReceiptBalanceDto> purchaseReceipts = query.PartyType == PaymentPartyType.Supplier
            ? await balanceReader.GetForSupplierAsync(query.ActiveShopId, query.PartyId, null, cancellationToken)
            : [];

        return new PartyBalancesDto(
            query.PartyType,
            query.PartyId,
            receipts.Sum(r => r.UnallocatedAmount),
            invoices,
            purchaseReceipts,
            receipts);
    }
}
