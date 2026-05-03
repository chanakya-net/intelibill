using ErrorOr;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;

namespace Intelibill.Application.Features.Sales.Services;

public sealed record SaleAggregation(
    Sale Sale,
    CustomerLedgerEntry? LedgerEntry,
    SaleDto Dto);

public interface ISaleAggregator
{
    Task<ErrorOr<SaleAggregation>> AggregateAsync(
        string invoiceNumber,
        Guid shopId,
        decimal paidAmount,
        decimal dueAmount,
        Guid actorUserId,
        Customer? resolvedCustomer,
        string? customerName,
        string? customerPhone,
        PaymentMethod paymentMethod,
        IReadOnlyList<MutatedSaleLine> mutatedLines,
        List<string> warnings,
        IReadOnlyDictionary<Guid, string> itemNameById,
        CancellationToken cancellationToken);
}
