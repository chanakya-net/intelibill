using ErrorOr;
using Intelibill.Application.Features.Customers.Services;
using Intelibill.Application.Features.Sales.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;

/// <summary>Resolves an offline invoice's customer and records the customer and credit-policy variances it shows.</summary>
public sealed class OfflineSaleCustomerReviewer(
    ICustomerResolver customerResolver,
    ICustomerLedgerEntryRepository customerLedgerEntryRepository)
{
    /// <summary>Resolves the invoice's customer and records how it differs from what the device captured.</summary>
    internal async Task<ErrorOr<Customer?>> ResolveCustomerAsync(
        OfflineSalePostingRequest request,
        List<string> warnings,
        List<ReconciliationIssue> pendingIssues,
        CancellationToken cancellationToken)
    {
        var sale = request.Sale;
        var customerRes = await customerResolver.ResolveAsync(
            request.ShopId,
            sale.CustomerId,
            sale.CustomerPhone,
            sale.DueAmount > 0,
            sale.PaymentMethod,
            cancellationToken);
        if (customerRes.IsError) return customerRes;

        OfflineSaleSyncHelpers.AddCustomerVarianceWarnings(
            request.ShopId,
            request.RecordedByUserId,
            request.ClientSaleId,
            request.DeviceId,
            sale,
            customerRes.Value,
            warnings,
            pendingIssues);
        return customerRes;
    }

    internal async Task ReviewCustomerCreditAsync(
        OfflineSalePostingRequest request,
        Customer customer,
        List<string> warnings,
        List<ReconciliationIssue> pendingIssues,
        CancellationToken cancellationToken)
    {
        var shopId = request.ShopId;
        var dueAmount = request.Sale.DueAmount;
        var exposure = await customerLedgerEntryRepository.GetCustomerBalanceAsync(shopId, customer.Id, cancellationToken);
        var credit = CustomerCreditPolicy.Evaluate(CustomerCreditState.Of(customer), exposure, dueAmount, null, null);
        if (credit.IsError)
        {
            OfflineSaleSyncHelpers.AddCreditPolicyViolation(
                shopId,
                request.RecordedByUserId,
                request.ClientSaleId,
                request.DeviceId,
                customer,
                exposure,
                dueAmount,
                credit.FirstError,
                warnings,
                pendingIssues);
        }
    }
}
