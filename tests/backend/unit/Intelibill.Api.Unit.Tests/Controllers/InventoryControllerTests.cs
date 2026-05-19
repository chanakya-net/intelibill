using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Inventory.Commands.AddInventory;
using Intelibill.Application.Features.Inventory.Commands.AddInventoryBatch;
using Intelibill.Application.Features.Inventory.Commands.ReassignBatchSupplier;
using Intelibill.Application.Features.Inventory.Commands.VoidAdjustment;
using Intelibill.Application.Features.Inventory.DTOs;
using Intelibill.Application.Features.Inventory.Queries.GetAvailableBatches;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class InventoryControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly InventoryController _controller;

    public InventoryControllerTests()
    {
        _controller = new InventoryController(_bus);
    }

    [Fact]
    public async Task AddInventory_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.AddInventory(CreateRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task AddInventory_WhenNoActiveShopClaim_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        var result = await _controller.AddInventory(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task AddInventory_WhenValid_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var request = CreateRequest();

        var dto = new AddInventoryResultDto(
            Guid.NewGuid(),
            request.ItemName,
            request.Barcode,
            Guid.NewGuid(),
            request.BatchNumber,
            request.Quantity,
            request.Quantity,
            request.SupplierId,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        _bus.InvokeAsync<ErrorOr<AddInventoryResultDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(dto);

        var result = await _controller.AddInventory(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<AddInventoryResultDto>>(
            Arg.Is<AddInventoryCommand>(c =>
                c.ActorUserId == userId
                && c.ActiveShopId == shopId
                && c.ItemName == request.ItemName
                && c.Barcode == request.Barcode
                && c.HsnCode == request.HsnCode
                && c.SupplierId == request.SupplierId
                && c.Quantity == request.Quantity
                && c.TotalPurchaseCost == request.TotalPurchaseCost),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAvailableBatches_WhenValid_ForwardsQrBarcode()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var barcode = CreateQrLikeBarcode();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<IReadOnlyList<AvailableBatchDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<IReadOnlyList<AvailableBatchDto>>>(Array.Empty<AvailableBatchDto>().ToList()));

        var result = await _controller.GetAvailableBatches(searchTerm: null, barcode: barcode, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<IReadOnlyList<AvailableBatchDto>>>(
            Arg.Is<GetAvailableBatchesQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.SearchTerm == barcode
                && q.IsBarcodeLookup),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAvailableBatches_WhenSearchTermProvided_UsesSearchMode()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var searchTerm = "apple";
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<IReadOnlyList<AvailableBatchDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<IReadOnlyList<AvailableBatchDto>>>(Array.Empty<AvailableBatchDto>().ToList()));

        var result = await _controller.GetAvailableBatches(searchTerm: searchTerm, barcode: null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<IReadOnlyList<AvailableBatchDto>>>(
            Arg.Is<GetAvailableBatchesQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.SearchTerm == searchTerm
                && !q.IsBarcodeLookup),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAvailableBatches_WhenSearchTermAndBarcodeProvided_PrioritizesSearchMode()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var searchTerm = "apple";
        var barcode = CreateQrLikeBarcode();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<IReadOnlyList<AvailableBatchDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<IReadOnlyList<AvailableBatchDto>>>(Array.Empty<AvailableBatchDto>().ToList()));

        var result = await _controller.GetAvailableBatches(searchTerm: searchTerm, barcode: barcode, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<IReadOnlyList<AvailableBatchDto>>>(
            Arg.Is<GetAvailableBatchesQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.SearchTerm == searchTerm
                && !q.IsBarcodeLookup),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddInventory_WhenItemIdentityConflicts_ReturnsBadRequest()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<AddInventoryResultDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Inventory.ItemIdentityConflict);

        var result = await _controller.AddInventory(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task AddInventoryBatch_WhenEmptyBatch_ReturnsBadRequest()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var result = await _controller.AddInventoryBatch(new AddInventoryBatchRequest(Array.Empty<AddInventoryBatchRowRequest>()), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task AddInventoryBatch_WhenSomeRowsFail_ReturnsSuccessAndFailureRows()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var rowResult = new AddInventoryResultDto(
            Guid.NewGuid(),
            "Rice",
            "111",
            Guid.NewGuid(),
            "B-1",
            10m,
            10m,
            null,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

        _bus.InvokeAsync<ErrorOr<AddInventoryBatchResultDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(
                new AddInventoryBatchResultDto(
                    2,
                    1,
                    1,
                    [new AddInventoryBatchSucceededRowDto("row-1", rowResult)],
                    [new AddInventoryBatchFailedRowDto(
                        "row-2",
                        "Dal",
                        "222",
                        [new AddInventoryBatchRowErrorDto(Errors.Inventory.ItemIdentityConflict.Code, Errors.Inventory.ItemIdentityConflict.Description)])]));

        var request = new AddInventoryBatchRequest(
            new[]
            {
                CreateBatchRow("row-1", "Rice", "111"),
                CreateBatchRow("row-2", "Dal", "222"),
            });

        var result = await _controller.AddInventoryBatch(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(207, objectResult.StatusCode);
        var payload = Assert.IsType<AddInventoryBatchResponse>(objectResult.Value);

        Assert.Equal(2, payload.RequestedCount);
        Assert.Equal(1, payload.SuccessCount);
        Assert.Equal(1, payload.FailedCount);
        Assert.Single(payload.Succeeded);
        Assert.Single(payload.Failed);
        Assert.Equal("row-2", payload.Failed[0].ClientRowId);

        await _bus.Received(1).InvokeAsync<ErrorOr<AddInventoryBatchResultDto>>(
            Arg.Any<AddInventoryBatchCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddInventoryBatch_WhenMoreThanHundredRows_ReturnsClearLimitMessage()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var rows = Enumerable.Range(1, 101)
            .Select(index => CreateBatchRow($"row-{index}", $"Item-{index}", $"BC-{index}"))
            .ToArray();

        var result = await _controller.AddInventoryBatch(new AddInventoryBatchRequest(rows), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);

        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("Only 100 items are allowed in a batch.", problem.Detail);
    }

    [Fact]
    public async Task ReassignBatchSupplier_WhenValid_DispatchesCommand()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var newSupplierId = Guid.NewGuid();

        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success);

        var result = await _controller.ReassignBatchSupplier(batchId, new ReassignBatchSupplierRequest(newSupplierId), CancellationToken.None);

        Assert.IsType<OkResult>(result);
        await _bus.Received(1).InvokeAsync<ErrorOr<Success>>(
            Arg.Is<ReassignBatchSupplierCommand>(c =>
                c.ActorUserId == userId
                && c.ActiveShopId == shopId
                && c.BatchId == batchId
                && c.NewSupplierId == newSupplierId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VoidAdjustment_WhenValid_DispatchesCommand()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var adjustmentId = Guid.NewGuid();

        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var dto = new VoidAdjustmentResultDto(
            adjustmentId,
            Guid.NewGuid(),
            7m,
            10m,
            7m,
            10m,
            DateTimeOffset.UtcNow);

        _bus.InvokeAsync<ErrorOr<VoidAdjustmentResultDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _controller.VoidAdjustment(
            adjustmentId,
            new VoidAdjustmentRequest("Entered twice"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<VoidAdjustmentResultDto>>(
            Arg.Is<VoidAdjustmentCommand>(c =>
                c.AdjustmentId == adjustmentId
                && c.ActorUserId == userId
                && c.ActiveShopId == shopId
                && c.Reason == "Entered twice"),
            Arg.Any<CancellationToken>());
    }

    private static AddInventoryRequest CreateRequest() =>
        new(
            ItemName: "Rice",
            Barcode: "111",
            ItemDescription: "Sona masuri",
            HsnCode: "0402",
            Uom: "kg",
            BatchNumber: "B-1",
            Quantity: 10m,
            TotalPurchaseCost: 800m,
            Mrp: 120m,
            SalesPrice: 110m,
            TaxRatePercent: 5m,
            TaxIncluded: false,
            ExpiryDate: null,
            ManufacturingDate: null,
            SupplierId: null,
            ReferenceNumber: "PO-123",
            Notes: "initial",
            PerformedAt: null);

    private static AddInventoryBatchRowRequest CreateBatchRow(string clientRowId, string itemName, string barcode) =>
        new(
            ClientRowId: clientRowId,
            ItemName: itemName,
            Barcode: barcode,
            ItemDescription: null,
            Uom: "kg",
            BatchNumber: "B-1",
            Quantity: 1m,
            TotalPurchaseCost: 80m,
            Mrp: 120m,
            SalesPrice: 110m,
            TaxRatePercent: 5m,
            TaxIncluded: false,
            ExpiryDate: null,
            ManufacturingDate: null,
            SupplierId: null,
            ReferenceNumber: null,
            Notes: null,
            PerformedAt: null,
            HsnCode: null);

    private static string CreateQrLikeBarcode() =>
        $"QR|01|{Guid.NewGuid():N}|TRACE|{Guid.NewGuid():N}|PAYLOAD|{new string('D', 24)}";

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
