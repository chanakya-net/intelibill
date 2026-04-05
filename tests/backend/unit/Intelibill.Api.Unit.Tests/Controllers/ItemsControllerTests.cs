using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Commands.AddItem;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Application.Features.Items.Queries.GetItems;
using Intelibill.Application.Features.Items.Queries.StreamItems;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class ItemsControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IItemRepository _itemRepository = Substitute.For<IItemRepository>();
    private readonly ItemsController _controller;

    public ItemsControllerTests()
    {
        _controller = new ItemsController(_bus, _itemRepository);
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

    [Fact]
    public async Task StreamItems_WhenNoUserClaim_Sets401()
    {
        SetUserClaims();
        SetResponseBody();

        await _controller.StreamItems(CancellationToken.None);

        Assert.Equal(401, _controller.Response.StatusCode);
    }

    [Fact]
    public async Task StreamItems_WhenNoActiveShopClaim_Sets400()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));
        SetResponseBody();

        await _controller.StreamItems(CancellationToken.None);

        Assert.Equal(400, _controller.Response.StatusCode);
    }

    [Fact]
    public async Task StreamItems_WhenValidationFails_Sets403()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));
        SetResponseBody();

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Errors.Shop.MembershipNotFound));

        await _controller.StreamItems(CancellationToken.None);

        Assert.Equal(403, _controller.Response.StatusCode);
    }

    [Fact]
    public async Task StreamItems_WhenValid_WritesNdjsonLines()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var responseBody = SetResponseBody();

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));

        var item1 = Item.Create(shopId, "Milk", null, "ltr", "B001", true, null, userId);
        var item2 = Item.Create(shopId, "Rice", "Premium", "kg", "B002", true, null, userId);
        _itemRepository.StreamByShopIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(AsyncItems(item1, item2));

        await _controller.StreamItems(CancellationToken.None);

        responseBody.Position = 0;
        var body = await new System.IO.StreamReader(responseBody).ReadToEndAsync();
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(2, lines.Length);
        Assert.Equal("application/x-ndjson; charset=utf-8", _controller.Response.ContentType);

        var first = JsonDocument.Parse(lines[0]).RootElement;
        Assert.Equal("Milk", first.GetProperty("name").GetString());
        Assert.Equal("B001", first.GetProperty("barcode").GetString());

        var second = JsonDocument.Parse(lines[1]).RootElement;
        Assert.Equal("Rice", second.GetProperty("name").GetString());
        Assert.Equal("B002", second.GetProperty("barcode").GetString());

        await _bus.Received(1).InvokeAsync<ErrorOr<Success>>(
            Arg.Is<StreamItemsQuery>(q => q.UserId == userId && q.ActiveShopId == shopId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StreamItems_WhenNoItems_WritesEmptyBody()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var responseBody = SetResponseBody();

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));

        _itemRepository.StreamByShopIdAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(AsyncItems());

        await _controller.StreamItems(CancellationToken.None);

        responseBody.Position = 0;
        var body = await new System.IO.StreamReader(responseBody).ReadToEndAsync();

        Assert.Empty(body.Trim());
    }

    private static AddItemRequest CreateRequest() =>
        new(
            Name: "Rice",
            Barcode: "111",
            Description: "Premium",
            Uom: "kg",
            IsActive: true,
            PreferredSupplierId: null);

    private System.IO.MemoryStream SetResponseBody()
    {
        var stream = new System.IO.MemoryStream();
        var existingUser = _controller.ControllerContext?.HttpContext?.User ?? new ClaimsPrincipal();
        var context = new DefaultHttpContext { User = existingUser };
        context.Response.Body = stream;
        _controller.ControllerContext = new ControllerContext { HttpContext = context };
        return stream;
    }

    private static async IAsyncEnumerable<Intelibill.Domain.Entities.Item> AsyncItems(
        params Intelibill.Domain.Entities.Item[] items)
    {
        foreach (var item in items)
            yield return item;
        await Task.CompletedTask;
    }

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
