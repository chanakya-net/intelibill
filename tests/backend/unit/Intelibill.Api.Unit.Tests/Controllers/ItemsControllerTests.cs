using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Commands.AddItem;
using Intelibill.Application.Features.Items.Commands.UpdateItem;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Application.Features.Items.Queries.GetProductDetails;
using Intelibill.Application.Features.Items.Queries.GetItems;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class ItemsControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IItemCatalogStreamingService _itemCatalogStreamingService = Substitute.For<IItemCatalogStreamingService>();
    private readonly ItemsController _controller;

    public ItemsControllerTests()
    {
        _controller = new ItemsController(_bus, _itemCatalogStreamingService);
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
            new ItemDto(Guid.NewGuid(), "Milk", "B001", null, "ltr", true, 10m),
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
    public async Task GetProductDetails_WhenValid_ForwardsAuthorizationHeader()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var barcode = CreateQrLikeBarcode();
        _controller.HttpContext.Request.Headers.Authorization = "Bearer token-123";

        var dto = new ProductDetailsDto(
            Name: "Rice",
            Description: "Premium",
            Uom: "kg",
            CostPrice: 80m,
            Mrp: 100m,
            SalesPrice: 95m,
            SupplierId: null,
            SupplierName: null,
            TaxIncluded: null,
            TaxRatePercent: null);

        _bus.InvokeAsync<ErrorOr<ProductDetailsDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _controller.GetProductDetails(name: null, barcode: barcode, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<ProductDetailsDto>>(
            Arg.Is<GetProductDetailsByNameOrBarcodeQuery>(q =>
                q.UserId == userId
                && q.ActiveShopId == shopId
                && q.ProductName == null
                && q.Barcode == barcode
                && q.AuthorizationHeader == "Bearer token-123"),
            Arg.Any<CancellationToken>());
    }

    private static string CreateQrLikeBarcode() =>
        $"QR|01|{Guid.NewGuid():N}|TRACE|{Guid.NewGuid():N}|PAYLOAD|{new string('C', 24)}";

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
        var dto = new ItemDto(Guid.NewGuid(), request.Name, request.Barcode, request.Description, request.Uom, request.IsActive, 0m);

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

        _itemCatalogStreamingService.ValidateAccessAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
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

        var item1 = new ItemCatalogEntryDto(Guid.NewGuid(), "Milk", "B001");
        var item2 = new ItemCatalogEntryDto(Guid.NewGuid(), "Rice", "B002");
        _itemCatalogStreamingService.ValidateAccessAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));
        _itemCatalogStreamingService.StreamByShopAsync(shopId, Arg.Any<CancellationToken>())
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

        await _itemCatalogStreamingService.Received(1).ValidateAccessAsync(
            Arg.Is<Guid>(id => id == userId),
            Arg.Is<Guid>(id => id == shopId),
            Arg.Any<CancellationToken>());
        _itemCatalogStreamingService.Received(1).StreamByShopAsync(
            Arg.Is<Guid>(id => id == shopId),
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

        _itemCatalogStreamingService.ValidateAccessAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));
        _itemCatalogStreamingService.StreamByShopAsync(shopId, Arg.Any<CancellationToken>())
            .Returns(AsyncItems());

        await _controller.StreamItems(CancellationToken.None);

        responseBody.Position = 0;
        var body = await new System.IO.StreamReader(responseBody).ReadToEndAsync();

        Assert.Empty(body.Trim());
    }

    [Fact]
    public async Task UpdateItem_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();
        var itemId = Guid.NewGuid();

        var result = await _controller.UpdateItem(itemId, CreateUpdateRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task UpdateItem_WhenValid_ReturnsNoContent()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var request = CreateUpdateRequest();

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));

        var result = await _controller.UpdateItem(itemId, request, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await _bus.Received(1).InvokeAsync<ErrorOr<Success>>(
            Arg.Is<UpdateItemCommand>(c =>
                c.ActorUserId == userId &&
                c.ActiveShopId == shopId &&
                c.ItemId == itemId &&
                c.Name == request.Name &&
                c.Barcode == request.Barcode),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateItem_WhenItemNotFound_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Item.ItemNotFound);

        var result = await _controller.UpdateItem(itemId, CreateUpdateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task UpdateItem_WhenBarcodeConflict_ReturnsConflict()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Item.BarcodeAlreadyExists);

        var result = await _controller.UpdateItem(itemId, CreateUpdateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
    }

    private static AddItemRequest CreateRequest() =>
        new(
            Name: "Rice",
            Barcode: "111",
            Description: "Premium",
            Uom: "kg",
            IsActive: true);

    private static UpdateItemRequest CreateUpdateRequest() =>
        new(
            Name: "Premium Rice",
            Barcode: "112",
            Description: "High Quality",
            Uom: "kg");

    private System.IO.MemoryStream SetResponseBody()
    {
        var stream = new System.IO.MemoryStream();
        var existingUser = _controller.ControllerContext?.HttpContext?.User ?? new ClaimsPrincipal();
        var context = new DefaultHttpContext { User = existingUser };
        context.Response.Body = stream;
        _controller.ControllerContext = new ControllerContext { HttpContext = context };
        return stream;
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

    private static async IAsyncEnumerable<ItemCatalogEntryDto> AsyncItems(params ItemCatalogEntryDto[] items)
    {
        foreach (var item in items)
            yield return item;
        await Task.CompletedTask;
    }
}
