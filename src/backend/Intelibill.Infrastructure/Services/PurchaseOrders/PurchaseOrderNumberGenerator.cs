using Intelibill.Application.Features.PurchaseOrders.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Infrastructure.Services.PurchaseOrders;

internal sealed class PurchaseOrderNumberGenerator(
    IPurchaseOrderSequenceRepository sequenceRepository,
    IUnitOfWork unitOfWork) : IPurchaseOrderNumberGenerator
{
    public async Task<string> GenerateAsync(Guid shopId, int year, CancellationToken cancellationToken = default)
    {
        var sequence = await sequenceRepository.GetOrCreateByShopAndYearAsync(shopId, year, cancellationToken);

        var number = sequence.GetAndIncrement();
        sequenceRepository.Update(sequence);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return $"PO-{year}-{number:D6}";
    }
}
