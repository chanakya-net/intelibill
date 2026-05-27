using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Queries.GetItems;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using NSubstitute;

namespace Intelibill.Application.Unit.Tests.Features.Items.Queries.GetItems;

public class GetItemsQueryHandlerTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IItemCatalogRepository _itemCatalogRepository = Substitute.For<IItemCatalogRepository>();

    [Fact]
    public async Task HandleAsync_WhenCallerNotInActiveShop_ReturnsForbidden()
    {
        var caller = User.CreateWithEmail("member@test.com", "hash", "Member", "One");
        _userRepository.GetByIdWithDetailsAsync(caller.Id, Arg.Any<CancellationToken>()).Returns(caller);

        var handler = new GetItemsQueryHandler(_userRepository, _itemCatalogRepository);
        var result = await handler.HandleAsync(new GetItemsQuery(caller.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(Errors.Shop.MembershipNotFound.Code, result.FirstError.Code);
    }

    [Fact]
    public async Task HandleAsync_WhenValid_ReturnsPagedCatalogAndSummary()
    {
        var owner = User.CreateWithEmail("owner@test.com", "hash", "Owner", "One");
        var manager = User.CreateWithEmail("manager@test.com", "hash", "Manager", "One");
        var shop = Shop.Create("Main", "Address", "City", "State", "560001", null, null, null);

        var ownerMembership = ShopMembership.Create(shop.Id, owner.Id, ShopRole.Owner, true);
        var managerMembership = ShopMembership.Create(shop.Id, manager.Id, ShopRole.Manager, false);
        shop.AddMembership(ownerMembership);
        shop.AddMembership(managerMembership);
        manager.AddShopMembership(managerMembership);

        _userRepository.GetByIdWithDetailsAsync(manager.Id, Arg.Any<CancellationToken>()).Returns(manager);

        var catalog = new ItemCatalogResultReadModel(
            Items:
            [
                new ItemCatalogReadModel(
                    Guid.NewGuid(),
                    "Milk",
                    "B001",
                    null,
                    "ltr",
                    true,
                    25m,
                    42m,
                    1050m,
                    10m,
                    "runningLow",
                    "0401",
                    5m,
                    false),
                new ItemCatalogReadModel(
                    Guid.NewGuid(),
                    "Rice",
                    "B002",
                    "Premium",
                    "kg",
                    true,
                    0m,
                    60m,
                    0m,
                    0m,
                    "critical",
                    null,
                    0m,
                    false),
            ],
            TotalCount: 2,
            Summary: new ItemCatalogSummaryReadModel(
                TotalItems: 2,
                ActiveItems: 2,
                InactiveItems: 0,
                RunningLowStockCount: 1,
                CriticalStockCount: 1,
                TotalStockValue: 1050m));

        ItemCatalogFilter? capturedFilter = null;
        _itemCatalogRepository.GetCatalogAsync(Arg.Do<ItemCatalogFilter>(filter => capturedFilter = filter), Arg.Any<CancellationToken>())
            .Returns(catalog);

        var handler = new GetItemsQueryHandler(_userRepository, _itemCatalogRepository);
        var result = await handler.HandleAsync(
            new GetItemsQuery(manager.Id, shop.Id, Search: "rice", Status: "active", PageNumber: 0, PageSize: 250),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(capturedFilter);
        Assert.Equal(shop.Id, capturedFilter!.ShopId);
        Assert.Equal("rice", capturedFilter.Search);
        Assert.Equal("active", capturedFilter.Status);
        Assert.Equal(1, capturedFilter.PageNumber);
        Assert.Equal(100, capturedFilter.PageSize);

        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(1, result.Value.PageNumber);
        Assert.Equal(100, result.Value.PageSize);
        Assert.Equal(2, result.Value.Items.Count);

        Assert.Equal("Milk", result.Value.Items[0].Name);
        Assert.Equal(25m, result.Value.Items[0].CurrentStock);
        Assert.Equal(42m, result.Value.Items[0].UnitPrice);
        Assert.Equal(1050m, result.Value.Items[0].CurrentStockValue);
        Assert.Equal(10m, result.Value.Items[0].ReorderLevel);
        Assert.Equal("runningLow", result.Value.Items[0].StockStatus);

        Assert.Equal(2, result.Value.Summary.TotalItems);
        Assert.Equal(2, result.Value.Summary.ActiveItems);
        Assert.Equal(0, result.Value.Summary.InactiveItems);
        Assert.Equal(1, result.Value.Summary.RunningLowStockCount);
        Assert.Equal(1, result.Value.Summary.CriticalStockCount);
        Assert.Equal(1050m, result.Value.Summary.TotalStockValue);
    }
}
