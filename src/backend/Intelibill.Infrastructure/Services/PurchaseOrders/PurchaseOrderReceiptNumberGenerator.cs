using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Infrastructure.Services.PurchaseOrders;

internal sealed class PurchaseOrderReceiptNumberGenerator(
    IPurchaseOrderReceiptSequenceRepository sequenceRepository) : IPurchaseOrderReceiptNumberGenerator
{
    public async Task<string> GenerateAsync(Guid shopId, int year, CancellationToken cancellationToken = default)
    {
        var sequence = await sequenceRepository.GetOrCreateByShopAndYearAsync(shopId, year, cancellationToken);
        var number = sequence.GetAndIncrement();
        sequenceRepository.Update(sequence);
        return $"POR-{year}-{number:D6}";
    }
}
