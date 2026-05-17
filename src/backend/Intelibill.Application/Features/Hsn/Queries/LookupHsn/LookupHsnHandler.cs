using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces;
using Intelibill.Domain.Interfaces.Repositories;

namespace Intelibill.Application.Features.Hsn.Queries.LookupHsn;

public sealed class LookupHsnHandler(
    IHsnCacheRepository hsnCacheRepository,
    IExternalHsnLookupService externalHsnLookupService,
    IUnitOfWork unitOfWork)
{
    public async Task<ErrorOr<LookupHsnResult>> HandleAsync(LookupHsnQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.ProductName))
            return Errors.Hsn.EmptyProductName;

        var normalizedName = query.ProductName.Trim();

        var cached = await hsnCacheRepository.GetByProductNameAsync(normalizedName, cancellationToken);
        if (cached is not null)
        {
            return Map(cached);
        }

        var external = await externalHsnLookupService.LookupAsync(normalizedName, cancellationToken);
        if (external.IsError)
            return external.Errors;

        if (!external.Value.Success || external.Value.Data is null)
            return Errors.Hsn.LookupFailed;

        var cacheEntry = HsnCache.Create(
            normalizedName,
            external.Value.Data.HsnCodes,
            external.Value.Data.TaxScenarios.Select(ts => new HsnTaxScenario(ts.Condition, ts.TaxPercentage)).ToArray());

        await hsnCacheRepository.SaveAsync(cacheEntry, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new LookupHsnResult(
            external.Value.Data.HsnCodes,
            external.Value.Data.TaxScenarios.Select(ts => new HsnTaxScenarioResult(ts.Condition, ts.TaxPercentage)).ToArray());
    }

    private static LookupHsnResult Map(HsnCache cache)
    {
        return new LookupHsnResult(
            cache.HsnCodes.ToArray(),
            cache.TaxScenarios.Select(ts => new HsnTaxScenarioResult(ts.Condition, ts.TaxPercentage)).ToArray());
    }
}
