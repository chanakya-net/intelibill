using Intelibill.Application.Features.Discounts.Services;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Discounts.Services;

public sealed class DiscountRuleValidationServiceTests
{
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IDiscountRuleRepository _discountRuleRepository = Substitute.For<IDiscountRuleRepository>();

    private DiscountRuleValidationService CreateService() => new(_batchRepository, _discountRuleRepository);

    private static InventoryBatch MakeBatch(Guid shopId)
    {
        var result = InventoryBatch.Create(
            shopId,
            Guid.NewGuid(),
            "B-01",
            50m,
            80m,
            110m,
            100m,
            0m,
            false,
            null,
            null,
            null,
            Guid.NewGuid());

        return result.Value;
    }

    [Fact]
    public async Task PreviewAsync_BatchRule_WhenOverlapsExisting_ReturnsOverlapError()
    {
        var shopId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var batch = MakeBatch(shopId);

        var existing = DiscountRule.Create(
            shopId,
            DiscountRuleType.BatchPercentage,
            "Existing",
            null,
            batchId,
            5m,
            null,
            startsAt: null,
            endsAt: null,
            belowCostConfirmed: false,
            belowCostConfirmationReason: null,
            createdBy: Guid.NewGuid()).Value;

        _batchRepository.GetByIdWithItemAsync(batchId, shopId, Arg.Any<CancellationToken>()).Returns(batch);
        _discountRuleRepository.GetAllActiveByBatchAsync(shopId, batchId, Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var result = await CreateService().PreviewAsync(
            shopId,
            DiscountRuleType.BatchPercentage,
            percentage: 10m,
            thresholdAmount: null,
            inventoryBatchId: batchId,
            startsAt: null,
            endsAt: null,
            belowCostConfirmed: true,
            excludeRuleId: null,
            cancellationToken: CancellationToken.None);

        Assert.Contains(result.Errors, e => e.Code == "discount.error.overlap");
    }

    [Fact]
    public async Task PreviewAsync_BatchRule_WhenExcludeRuleIdMatches_SkipsSelfOverlap()
    {
        var shopId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var batch = MakeBatch(shopId);

        var existing = DiscountRule.Create(
            shopId,
            DiscountRuleType.BatchPercentage,
            "Existing",
            null,
            batchId,
            5m,
            null,
            startsAt: null,
            endsAt: null,
            belowCostConfirmed: false,
            belowCostConfirmationReason: null,
            createdBy: Guid.NewGuid()).Value;

        _batchRepository.GetByIdWithItemAsync(batchId, shopId, Arg.Any<CancellationToken>()).Returns(batch);
        _discountRuleRepository.GetAllActiveByBatchAsync(shopId, batchId, Arg.Any<CancellationToken>())
            .Returns(new[] { existing });

        var result = await CreateService().PreviewAsync(
            shopId,
            DiscountRuleType.BatchPercentage,
            percentage: 10m,
            thresholdAmount: null,
            inventoryBatchId: batchId,
            startsAt: null,
            endsAt: null,
            belowCostConfirmed: true,
            excludeRuleId: existing.Id,
            cancellationToken: CancellationToken.None);

        Assert.DoesNotContain(result.Errors, e => e.Code == "discount.error.overlap");
    }
}

