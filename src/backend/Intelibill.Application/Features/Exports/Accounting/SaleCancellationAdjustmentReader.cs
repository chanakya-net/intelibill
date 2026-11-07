using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Domain.Entities;

namespace Intelibill.Application.Features.Exports.Accounting;

public sealed class SaleCancellationAdjustmentReader(ISaleCancellationRepository repository)
{
    public async Task<IReadOnlyList<SaleCancellationAdjustmentEntry>> ReadAsync(
        Guid shopId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var facts = await repository.GetPostedInRangeAsync(shopId, from, to, cancellationToken);
        return Project(facts.Where(c => c.BusinessDate >= from && c.BusinessDate <= to));
    }

    public async Task<IReadOnlyList<SaleCancellationAdjustmentEntry>> ReadForSalesAsync(Guid shopId, IReadOnlyCollection<Guid> saleIds, CancellationToken cancellationToken) =>
        Project(await repository.GetBySaleIdsAsync(shopId, saleIds, cancellationToken));

    private static List<SaleCancellationAdjustmentEntry> Project(IEnumerable<SaleCancellation> facts) =>
        facts.Select(c => new SaleCancellationAdjustmentEntry(c.Id, c.SaleId, c.ReferenceNumber,
                c.OriginalInvoiceNumber, c.OriginalBusinessDate, c.BusinessDate, c.CancelledAt,
                c.BusinessTimeZoneId, c.Reason, c.PolicyVersion, c.Seller, c.Buyer,
                c.TotalAmount, c.RevenueAmount, c.Lines.Sum(l => l.Quantity * l.OriginalCostPrice),
                c.Lines.Sum(l => l.RecordedCostAmount), c.DueReductionAmount, c.CashRefundAmount, c.Lines)).ToList();
}
