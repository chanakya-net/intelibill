using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Discounts.Queries.PreviewDiscountRule;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Discounts.Queries.PreviewDiscountRule;

public class PreviewDiscountRuleQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IInventoryBatchRepository _batchRepository = Substitute.For<IInventoryBatchRepository>();
    private readonly IDiscountRuleRepository _discountRuleRepository = Substitute.For<IDiscountRuleRepository>();

    private PreviewDiscountRuleQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _batchRepository, _discountRuleRepository);

    private static User MakeUser() =>
        User.CreateWithEmail("discount@test.com", "hash", "Test", "User");

    private static Shop MakeShop() =>
        Shop.Create("Test Shop", "123 St", "City", "State", "560001", null, null, null);

    private static ShopMembership MakeMembership(Guid shopId, Guid userId) =>
        ShopMembership.Create(shopId, userId, ShopRole.Owner, true);

    private static InventoryBatch MakeBatch(Guid shopId, decimal costPrice = 80m, decimal salesPrice = 100m, bool voided = false)
    {
        var result = InventoryBatch.Create(shopId, Guid.NewGuid(), "B-01",
            50m, costPrice, salesPrice * 1.1m, salesPrice, 0m, false, null, null, null, Guid.NewGuid());
        var batch = result.Value;
        if (voided) batch.Void(Guid.NewGuid());
        return batch;
    }

    private void SetupAuthSuccess(User user, Shop shop, ShopMembership membership)
    {
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns(membership);
    }

    [Fact]
    public async Task Handle_WhenUserNotFound_ReturnsNotFoundError()
    {
        _userRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(Guid.NewGuid(), Guid.NewGuid(), DiscountRuleType.SalePercentage,
                10m, null, null, null, null, false),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("User.NotFound", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenShopNotFound_ReturnsShopNotFoundError()
    {
        var user = MakeUser();
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Shop?)null);

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, Guid.NewGuid(), DiscountRuleType.SalePercentage,
                10m, null, null, null, null, false),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.ShopNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenMembershipNotFound_ReturnsMembershipNotFoundError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _shopRepository.GetByIdAsync(shop.Id, Arg.Any<CancellationToken>()).Returns(shop);
        _shopRepository.GetMembershipAsync(user.Id, shop.Id, Arg.Any<CancellationToken>()).Returns((ShopMembership?)null);

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SalePercentage,
                10m, null, null, null, null, false),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_WhenPercentageIsZero_ReturnsPreviewWithPercentageError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SalePercentage,
                0m, null, null, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Errors);
        Assert.Equal("discount.error.percent_out_of_range", result.Value.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_WhenPercentageExceeds100_ReturnsPreviewWithPercentageError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SalePercentage,
                101m, null, null, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Errors);
        Assert.Equal("discount.error.percent_out_of_range", result.Value.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_WhenWindowInvalid_ReturnsPreviewWithWindowError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);
        var now = DateTimeOffset.UtcNow;

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SalePercentage,
                10m, null, null, now.AddDays(2), now.AddDays(1), false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Errors);
        Assert.Equal("discount.error.window_invalid", result.Value.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_SaleLevelRule_WhenNoSellableBatches_ReturnsInfoWithZeroCount()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        _batchRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<InventoryBatch>());

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SalePercentage,
                10m, null, null, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(0, result.Value.AffectedCount);
        Assert.Single(result.Value.Infos);
        Assert.Equal("discount.info.no_affected_batches", result.Value.Infos[0].Code);
    }

    [Fact]
    public async Task Handle_SaleLevelRule_WhenBatchesExist_ReturnsAffectedCount()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var batch1 = MakeBatch(shop.Id, costPrice: 80m, salesPrice: 100m);
        var batch2 = MakeBatch(shop.Id, costPrice: 50m, salesPrice: 90m);

        _batchRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { batch1, batch2 });

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SalePercentage,
                5m, null, null, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(2, result.Value.AffectedCount);
        Assert.Empty(result.Value.Errors);
        Assert.Empty(result.Value.BelowCostSample);
        Assert.NotNull(result.Value.SafeMaxPercentage);
    }

    [Fact]
    public async Task Handle_SaleLevelRule_WhenDiscountBelowCostAndNotConfirmed_ReturnsConfirmationError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var batch = MakeBatch(shop.Id, costPrice: 95m, salesPrice: 100m);

        _batchRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { batch });

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SalePercentage,
                20m, null, null, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Errors);
        Assert.Equal("discount.error.below_cost_confirmation_required", result.Value.Errors[0].Code);
        Assert.Single(result.Value.BelowCostSample);
    }

    [Fact]
    public async Task Handle_SaleLevelRule_WhenDiscountBelowCostAndConfirmed_NoConfirmationError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var batch = MakeBatch(shop.Id, costPrice: 95m, salesPrice: 100m);

        _batchRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { batch });

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SalePercentage,
                20m, null, null, null, null, true),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Errors);
        Assert.Single(result.Value.BelowCostSample);
    }

    [Fact]
    public async Task Handle_SaleLevelRule_SafeMaxIsMinAcrossBatches()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        // batch1: salesPrice=100, costPrice=80 → safeMax = floor((1-80/100)*10000)/100 = 20.00
        // batch2: salesPrice=100, costPrice=90 → safeMax = floor((1-90/100)*10000)/100 = 10.00
        // min = 10.00
        var batch1 = MakeBatch(shop.Id, costPrice: 80m, salesPrice: 100m);
        var batch2 = MakeBatch(shop.Id, costPrice: 90m, salesPrice: 100m);

        _batchRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { batch1, batch2 });

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SalePercentage,
                5m, null, null, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(10.00m, result.Value.SafeMaxPercentage);
    }

    [Fact]
    public async Task Handle_SaleLevelRule_CapsAffectedSampleAtFive()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var batches = Enumerable.Range(1, 7)
            .Select(_ => MakeBatch(shop.Id, costPrice: 80m, salesPrice: 100m))
            .ToArray();

        _batchRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(batches);

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SalePercentage,
                5m, null, null, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(7, result.Value.AffectedCount);
        Assert.Equal(5, result.Value.AffectedSample.Count);
    }

    [Fact]
    public async Task Handle_BatchRule_WhenBatchIdMissing_ReturnsPreviewWithBatchRequiredError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.BatchPercentage,
                10m, null, null, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Errors);
        Assert.Equal("discount.error.batch_required", result.Value.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_BatchRule_WhenBatchNotFound_ReturnsPreviewWithBatchNotFoundError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        _batchRepository.GetByIdWithItemAsync(Arg.Any<Guid>(), shop.Id, Arg.Any<CancellationToken>())
            .Returns((InventoryBatch?)null);

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.BatchPercentage,
                10m, null, Guid.NewGuid(), null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Errors);
        Assert.Equal("discount.error.batch_not_found", result.Value.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_BatchRule_WhenBatchIsVoided_ReturnsPreviewWithVoidedError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var batchId = Guid.NewGuid();
        var voidedBatch = MakeBatch(shop.Id, voided: true);

        _batchRepository.GetByIdWithItemAsync(batchId, shop.Id, Arg.Any<CancellationToken>())
            .Returns(voidedBatch);
        _discountRuleRepository.GetAllActiveByBatchAsync(shop.Id, batchId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DiscountRule>());

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.BatchPercentage,
                10m, null, batchId, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Errors, e => e.Code == "discount.error.batch_voided");
        Assert.Equal(0, result.Value.AffectedCount);
        Assert.Empty(result.Value.AffectedSample);
        Assert.Empty(result.Value.BelowCostSample);
    }

    [Fact]
    public async Task Handle_BatchRule_WhenBatchIsVoidedAndBelowCost_BelowCostSampleIsEmpty()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var batchId = Guid.NewGuid();
        // costPrice > discountedPrice so it would be below-cost if the batch were active
        var voidedBatch = MakeBatch(shop.Id, costPrice: 95m, salesPrice: 100m, voided: true);

        _batchRepository.GetByIdWithItemAsync(batchId, shop.Id, Arg.Any<CancellationToken>())
            .Returns(voidedBatch);
        _discountRuleRepository.GetAllActiveByBatchAsync(shop.Id, batchId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DiscountRule>());

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.BatchPercentage,
                20m, null, batchId, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(0, result.Value.AffectedCount);
        Assert.Empty(result.Value.AffectedSample);
        Assert.Empty(result.Value.BelowCostSample);
        Assert.Contains(result.Value.Errors, e => e.Code == "discount.error.batch_voided");
    }

    [Fact]
    public async Task Handle_BatchRule_WhenOverlappingWindowExists_ReturnsOverlapError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var batchId = Guid.NewGuid();
        var batch = MakeBatch(shop.Id, costPrice: 80m, salesPrice: 100m);

        _batchRepository.GetByIdWithItemAsync(batchId, shop.Id, Arg.Any<CancellationToken>())
            .Returns(batch);

        // Existing rule with open-ended window (always overlaps)
        var existingRule = DiscountRule.Create(
            shop.Id, DiscountRuleType.BatchPercentage, "Existing Rule", null,
            batchId, 5m, null, null, null, false, null, user.Id).Value;

        _discountRuleRepository.GetAllActiveByBatchAsync(shop.Id, batchId, Arg.Any<CancellationToken>())
            .Returns(new[] { existingRule });

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.BatchPercentage,
                10m, null, batchId, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Errors, e => e.Code == "discount.error.overlap");
    }

    [Fact]
    public async Task Handle_BatchRule_WhenNonOverlappingWindowExists_NoOverlapError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var batchId = Guid.NewGuid();
        var batch = MakeBatch(shop.Id, costPrice: 80m, salesPrice: 100m);
        var now = DateTimeOffset.UtcNow;

        _batchRepository.GetByIdWithItemAsync(batchId, shop.Id, Arg.Any<CancellationToken>())
            .Returns(batch);

        // Existing rule ends yesterday — no overlap with a new rule starting tomorrow
        var existingRule = DiscountRule.Create(
            shop.Id, DiscountRuleType.BatchPercentage, "Past Rule", null,
            batchId, 5m, null, now.AddDays(-10), now.AddDays(-1), false, null, user.Id).Value;

        _discountRuleRepository.GetAllActiveByBatchAsync(shop.Id, batchId, Arg.Any<CancellationToken>())
            .Returns(new[] { existingRule });

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.BatchPercentage,
                10m, null, batchId, now.AddDays(1), now.AddDays(10), false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.DoesNotContain(result.Value.Errors, e => e.Code == "discount.error.overlap");
    }

    [Fact]
    public async Task Handle_BatchRule_WhenDiscountBelowCostAndNotConfirmed_ReturnsBelowCostError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var batchId = Guid.NewGuid();
        var batch = MakeBatch(shop.Id, costPrice: 95m, salesPrice: 100m);

        _batchRepository.GetByIdWithItemAsync(batchId, shop.Id, Arg.Any<CancellationToken>())
            .Returns(batch);
        _discountRuleRepository.GetAllActiveByBatchAsync(shop.Id, batchId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DiscountRule>());

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.BatchPercentage,
                20m, null, batchId, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains(result.Value.Errors, e => e.Code == "discount.error.below_cost_confirmation_required");
        Assert.Single(result.Value.BelowCostSample);
    }

    [Fact]
    public async Task Handle_BatchRule_ValidAboveCostNoOverlap_ReturnsCleanPreview()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var batchId = Guid.NewGuid();
        var batch = MakeBatch(shop.Id, costPrice: 80m, salesPrice: 100m);

        _batchRepository.GetByIdWithItemAsync(batchId, shop.Id, Arg.Any<CancellationToken>())
            .Returns(batch);
        _discountRuleRepository.GetAllActiveByBatchAsync(shop.Id, batchId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DiscountRule>());

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.BatchPercentage,
                10m, null, batchId, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Errors);
        Assert.Equal(1, result.Value.AffectedCount);
        Assert.Single(result.Value.AffectedSample);
        Assert.Empty(result.Value.BelowCostSample);
        Assert.NotNull(result.Value.SafeMaxPercentage);
        Assert.Equal(90m, result.Value.AffectedSample[0].DiscountedPrice); // 100 * 0.9
    }

    [Fact]
    public async Task Handle_WhenUnknownRuleType_ReturnsValidationError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, (DiscountRuleType)999,
                10m, null, null, null, null, false),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Discount.UnsupportedRuleType", result.FirstError.Code);
    }

    [Fact]
    public async Task Handle_SaleThresholdRule_WhenThresholdAmountMissing_ReturnsThresholdError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SaleThresholdPercentage,
                10m, null, null, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Errors);
        Assert.Equal("discount.error.threshold_required", result.Value.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_SaleThresholdRule_WhenThresholdAmountZero_ReturnsThresholdError()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SaleThresholdPercentage,
                10m, 0m, null, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Single(result.Value.Errors);
        Assert.Equal("discount.error.threshold_required", result.Value.Errors[0].Code);
    }

    [Fact]
    public async Task Handle_SaleThresholdRule_WhenThresholdAmountProvided_ProceedsLikeSaleRule()
    {
        var user = MakeUser();
        var shop = MakeShop();
        var membership = MakeMembership(shop.Id, user.Id);
        SetupAuthSuccess(user, shop, membership);

        var batch = MakeBatch(shop.Id, costPrice: 80m, salesPrice: 100m);
        _batchRepository.GetByShopAsync(shop.Id, Arg.Any<CancellationToken>())
            .Returns(new[] { batch });

        var result = await CreateHandler().Handle(
            new PreviewDiscountRuleQuery(user.Id, shop.Id, DiscountRuleType.SaleThresholdPercentage,
                10m, 500m, null, null, null, false),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Empty(result.Value.Errors);
        Assert.Equal(1, result.Value.AffectedCount);
    }
}
