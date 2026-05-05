using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Queries.GetAdjustmentHistory;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Inventory.Queries.GetAdjustmentHistory;

public sealed class GetAdjustmentHistoryQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IShopRepository _shopRepository = Substitute.For<IShopRepository>();
    private readonly IInventoryAdjustmentRepository _adjustmentRepository = Substitute.For<IInventoryAdjustmentRepository>();

    private GetAdjustmentHistoryQueryHandler CreateHandler() =>
        new(_userRepository, _shopRepository, _adjustmentRepository);

    [Fact]
    public async Task AdjustmentHistory_WhenStaffIsShopMember_ReturnsMappedPagedResults()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var adjustmentId = Guid.NewGuid();
        var performedAt = new DateTimeOffset(2026, 5, 1, 10, 30, 0, TimeSpan.Zero);
        var createdAt = performedAt.AddMinutes(1);
        var voidedAt = performedAt.AddHours(1);
        var voidedBy = Guid.NewGuid();
        var reversalTransactionId = Guid.NewGuid();

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("staff@test.com", "hash", "Staff", "Member"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("Main", "Address", "City", "State", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Staff, true));

        _adjustmentRepository.GetHistoryAsync(
                Arg.Is<InventoryAdjustmentHistoryFilter>(filter =>
                    filter.ShopId == shopId
                    && filter.PageNumber == 1
                    && filter.PageSize == 25
                    && filter.IncludeVoided),
                Arg.Any<CancellationToken>())
            .Returns(([
                new InventoryAdjustmentHistoryReadModel(
                    adjustmentId,
                    "ADJ-20260501-0001",
                    itemId,
                    "Toor Dal",
                    "TOOR-001",
                    batchId,
                    "B-001",
                    InventoryAdjustmentDirection.Decrease,
                    InventoryAdjustmentReason.Damaged,
                    3m,
                    80m,
                    240m,
                    "Damaged in storage",
                    performedAt,
                    createdAt,
                    userId,
                    "staff@test.com",
                    true,
                    voidedAt,
                    voidedBy,
                    "owner@test.com",
                    "Wrong count",
                    reversalTransactionId)
            ], 1));

        var result = await CreateHandler().Handle(
            new GetAdjustmentHistoryQuery(userId, shopId, IncludeVoided: true),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal(1, result.Value.PageNumber);
        Assert.Equal(25, result.Value.PageSize);

        var dto = Assert.Single(result.Value.Items);
        Assert.Equal(adjustmentId, dto.AdjustmentId);
        Assert.Equal("ADJ-20260501-0001", dto.AdjustmentNumber);
        Assert.Equal(itemId, dto.ItemId);
        Assert.Equal("Toor Dal", dto.ItemName);
        Assert.Equal("TOOR-001", dto.Barcode);
        Assert.Equal(batchId, dto.BatchId);
        Assert.Equal("B-001", dto.BatchNumber);
        Assert.Equal("Decrease", dto.Direction);
        Assert.Equal("Damaged", dto.Reason);
        Assert.Equal(3m, dto.Quantity);
        Assert.Equal(80m, dto.UnitCost);
        Assert.Equal(240m, dto.CostImpact);
        Assert.Equal("Damaged in storage", dto.Notes);
        Assert.Equal(performedAt, dto.PerformedAt);
        Assert.Equal(createdAt, dto.CreatedAt);
        Assert.Equal(userId, dto.PerformedByUserId);
        Assert.Equal("staff@test.com", dto.PerformedByDisplayName);
        Assert.True(dto.IsVoided);
        Assert.Equal(voidedAt, dto.VoidedAt);
        Assert.Equal(voidedBy, dto.VoidedByUserId);
        Assert.Equal("owner@test.com", dto.VoidedByDisplayName);
        Assert.Equal("Wrong count", dto.VoidReason);
        Assert.Equal(reversalTransactionId, dto.ReversalStockTransactionId);
    }

    [Fact]
    public async Task AdjustmentHistory_WhenPageInputsAreOutOfRange_NormalizesBeforeRepositoryCall()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("owner@test.com", "hash", "Owner", "User"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("Main", "Address", "City", "State", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns(ShopMembership.Create(shopId, userId, ShopRole.Owner, true));
        _adjustmentRepository.GetHistoryAsync(Arg.Any<InventoryAdjustmentHistoryFilter>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<InventoryAdjustmentHistoryReadModel>(), 0));

        var result = await CreateHandler().Handle(
            new GetAdjustmentHistoryQuery(userId, shopId, PageNumber: -2, PageSize: 250),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(1, result.Value.PageNumber);
        Assert.Equal(100, result.Value.PageSize);
        await _adjustmentRepository.Received(1).GetHistoryAsync(
            Arg.Is<InventoryAdjustmentHistoryFilter>(filter => filter.PageNumber == 1 && filter.PageSize == 100),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdjustmentHistory_WhenUserIsNotShopMember_ReturnsMembershipError()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();

        _userRepository.GetByIdAsync(userId, Arg.Any<CancellationToken>())
            .Returns(User.CreateWithEmail("outsider@test.com", "hash", "Out", "Sider"));
        _shopRepository.GetByIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(Shop.Create("Main", "Address", "City", "State", "560001", null, null, null));
        _shopRepository.GetMembershipAsync(userId, shopId, Arg.Any<CancellationToken>())
            .Returns((ShopMembership?)null);

        var result = await CreateHandler().Handle(
            new GetAdjustmentHistoryQuery(userId, shopId),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
        await _adjustmentRepository.DidNotReceive()
            .GetHistoryAsync(Arg.Any<InventoryAdjustmentHistoryFilter>(), Arg.Any<CancellationToken>());
    }
}
