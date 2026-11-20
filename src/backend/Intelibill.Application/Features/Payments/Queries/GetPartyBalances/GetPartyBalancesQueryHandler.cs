using Intelibill.Application.Common.Licensing;
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
    IPaymentTransactionRunner transactions,
    IShopLicenceFence fence,
    PaymentCompletionPolicy completionPolicy)
{
    [LicenceAction(LicenceAction.History)]
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

        return await transactions.RunAsync<PartyBalancesDto>(null, query.ActorUserId, query.PartyId, null,
            ct => PreviewAsync(query, ct), cancellationToken);
    }

    private async Task<ErrorOr<PartyBalancesDto>> PreviewAsync(GetPartyBalancesQuery query, CancellationToken ct)
    {
        var lease = await fence.EnterAsync(query.ActiveShopId, LicenceAction.DueSettlement, ct);
        if (lease.IsError) return lease.Errors;
        if (PaymentCompletionPolicy.Denial(lease.Value.CurrentDecision()) is { } denied) return denied;

        var balances = await completionPolicy.ReadBalancesAsync(query.ActiveShopId, query.PartyType, query.PartyId, ct);
        var decision = lease.Value.CurrentDecision();
        if (PaymentCompletionPolicy.Denial(decision) is { } readDenial) return readDenial;
        if (!PaymentCompletionPolicy.RequiresProof(decision)) return balances;

        var limit = await completionPolicy.RemainingDueAsync(query.ActiveShopId, balances, query.PaymentDate, ct);
        if (limit.IsError) return limit.Errors;
        if (PaymentCompletionPolicy.Denial(lease.Value.CurrentDecision()) is { } limitDenial) return limitDenial;
        return balances with { RemainingSettlementDue = limit.Value.Amount };
    }
}
