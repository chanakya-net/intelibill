using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Commands.AddItem;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Application.Features.Items.Queries.GetItems;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class ItemsControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly ItemsController _controller;

    public ItemsControllerTests()
    {
        _controller = new ItemsController(_bus, _userRepository, _itemRepository);
    }

    [Fact]
    public async Task GetItems_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.GetItems(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetItems_WhenValid_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var items = (IReadOnlyList<ItemDto>)
        [
            new ItemDto(Guid.NewGuid(), "Milk", "B001", null, "ltr", true, null),
        ];

        _bus.InvokeAsync<ErrorOr<IReadOnlyList<ItemDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<IReadOnlyList<ItemDto>>>(items.ToList()));

        var result = await _controller.GetItems(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(items, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<IReadOnlyList<ItemDto>>>(
            Arg.Is<GetItemsQuery>(q => q.UserId == userId && q.ActiveShopId == shopId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddItem_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.AddItem(CreateRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task AddItem_WhenValid_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var request = CreateRequest();
        var dto = new ItemDto(Guid.NewGuid(), request.Name, request.Barcode, request.Description, request.Uom, request.IsActive, request.PreferredSupplierId);

        _bus.InvokeAsync<ErrorOr<ItemDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(dto);

        var result = await _controller.AddItem(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<ItemDto>>(
            Arg.Is<AddItemCommand>(c => c.ActorUserId == userId && c.ActiveShopId == shopId && c.Name == request.Name && c.Barcode == request.Barcode),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddItem_WhenDuplicateBarcode_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<ItemDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Item.BarcodeAlreadyExists);

        var result = await _controller.AddItem(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
    }

    private static AddItemRequest CreateRequest() =>
        new(
            Name: "Rice",
            Barcode: "111",
            Description: "Premium",
            Uom: "kg",
            IsActive: true,
            PreferredSupplierId: null);

    private void SetUserClaims(params Claim[] claims)
    {
        var identity = claims.Length == 0
            ? new ClaimsIdentity()
            : new ClaimsIdentity(claims, "TestAuthType");

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            },
        };
    }
}
