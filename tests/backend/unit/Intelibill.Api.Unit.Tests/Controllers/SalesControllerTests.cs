using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Features.Sales.Commands.RecordSale;
using Intelibill.Application.Features.Sales.Commands.RecordSaleReturn;
using Intelibill.Application.Features.Sales.Commands.ReserveInvoiceLease;
using Intelibill.Application.Features.Sales.Commands.SyncOfflineSales;
using Intelibill.Application.Features.Sales.Commands.VoidSaleReturn;
using Intelibill.Application.Features.Sales.DTOs;
using Intelibill.Application.Features.Sales.Queries.SearchSellables;
using Intelibill.Application.Features.Sales.Queries.GetSaleByReturnNumber;
using Intelibill.Application.Features.Sales.Queries.GetSales;
using Intelibill.Application.Features.Sales.Queries.GetSaleDetail;
using Intelibill.Application.Features.Sales.Queries.PreviewSale;
using Intelibill.Application.Features.Sales.Queries.PreviewSaleReturn;
using Intelibill.Domain.Enums;
using Intelibill.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class SalesControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly IOfflineSalesSnapshotStreamingService _offlineSnapshotStreamingService =
        Substitute.For<IOfflineSalesSnapshotStreamingService>();
    private readonly SalesController _controller;

    public SalesControllerTests()
    {
        _controller = new SalesController(_bus, _offlineSnapshotStreamingService);
    }

    private void SetUserClaims(params Claim[] claims)
    {
        var identity = claims.Length == 0
            ? new ClaimsIdentity()
            : new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal },
        };
    }

    private static RecordSaleRequest CreateRequest(
        Guid? inventoryBatchId = null,
        IReadOnlyList<CreditNoteRedemptionRequest>? creditNoteRedemptions = null) =>
        new(
            null,
            "Ravi Kumar",
            "+919876543210",
            $"sale-{Guid.NewGuid():N}",
            PaymentMethod.Cash,
            500m,
            0m,
            [new RecordSaleItemRequest("BC-001", "B-01", "Rice", 5m, 80m, 100m, 120m, 18m, false, inventoryBatchId ?? Guid.NewGuid())],
            CreditNoteRedemptions: creditNoteRedemptions ?? []);

    private static OfflineSalesSyncRequest CreateOfflineSyncRequest(Guid? batchId = null) =>
        new(
            "device-1",
            [
                new OfflineSaleSyncRequest(
                    $"offline-{Guid.NewGuid():N}",
                    "INV-2025-26-000001",
                    DateTimeOffset.UtcNow,
                    null,
                    "Ravi Kumar",
                    "+919876543210",
                    PaymentMethod.Cash,
                    118m,
                    0m,
                    100m,
                    118m,
                    0m,
                    18m,
                    118m,
                    InstantDiscountType.None,
                    0m,
                    null,
                    null,
                    null,
                    null,
                    [
                        new OfflineSaleSyncLineRequest(
                            "BC-001",
                            "B-01",
                            "Rice",
                            1m,
                            80m,
                            100m,
                            120m,
                            18m,
                            false,
                            batchId ?? Guid.NewGuid(),
                            100m,
                            0m,
                            0m,
                            100m,
                            18m,
                            118m,
                            null,
                            null,
                            InstantDiscountType.None,
                            0m,
                            null)
                    ])
            ]);

    private static SaleDto CreateDto() =>
        new(
            Guid.NewGuid(),
            "INV-20260416-ABCD1234",
            null,
            "Ravi Kumar",
            "+919876543210",
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            500m,
            0m,
            500m,
            0m,
            500m,
            45m,
            0m,
            [],
            []);

    private static InvoiceLeaseDto CreateLeaseDto(Guid shopId, string deviceId) =>
        new(
            Guid.NewGuid(),
            shopId,
            deviceId,
            "2025-26",
            "INV-2025-26-",
            6,
            1,
            200,
            1,
            200,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(7));

    private static IReadOnlyList<SellableDto> CreateSellables() =>
        [
            SellableDto.FromInventoryBatch(
                Guid.NewGuid(),
                "BC-001",
                "Rice",
                "B-01",
                5m,
                100m,
                120m,
                18m,
                false,
                false,
                null),
            SellableDto.FromService(
                Guid.NewGuid(),
                "SVC-001",
                "Consulting",
                "Advice",
                250m,
                "9988",
                18m,
                false)
        ];

    [Fact]
    public async Task RecordSale_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.RecordSale(CreateRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task SyncOfflineSales_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.SyncOfflineSales(CreateOfflineSyncRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task ReserveInvoiceLease_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.ReserveInvoiceLease(new ReserveInvoiceLeaseRequest("device-1"), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task ReserveInvoiceLease_WhenNoActiveShopClaim_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        var result = await _controller.ReserveInvoiceLease(new ReserveInvoiceLeaseRequest("device-1"), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task ReserveInvoiceLease_WhenValid_ReturnsOkAndDispatchesCommand()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var dto = CreateLeaseDto(shopId, "device-1");
        _bus.InvokeAsync<ErrorOr<InvoiceLeaseDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _controller.ReserveInvoiceLease(new ReserveInvoiceLeaseRequest("device-1", 200), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<InvoiceLeaseDto>>(
            Arg.Is<ReserveInvoiceLeaseCommand>(c =>
                c.ActorUserId == userId
                && c.ShopId == shopId
                && c.DeviceId == "device-1"
                && c.BlockSize == 200),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSale_WhenNoActiveShopClaim_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        var result = await _controller.RecordSale(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task SyncOfflineSales_WhenNoActiveShopClaim_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        var result = await _controller.SyncOfflineSales(CreateOfflineSyncRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task RecordSale_WhenValid_ReturnsCreated()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var request = CreateRequest(batchId);
        var dto = CreateDto();
        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _controller.RecordSale(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        Assert.Equal(dto, createdResult.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<SaleDto>>(
            Arg.Is<RecordSaleCommand>(c =>
                c.ActorUserId == userId
                && c.ShopId == shopId
                && !string.IsNullOrWhiteSpace(c.IdempotencyKey)
                && c.PaymentMethod == PaymentMethod.Cash
                && c.PaidAmount == 500m
                && c.DueAmount == 0m
                && c.Items.Count == 1
                && c.Items[0].Barcode == "BC-001"
                && c.Items[0].InventoryBatchId == batchId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSale_WhenCreditNoteRedemptionProvided_PreservesClientAppliedAmount()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var request = CreateRequest(
            batchId,
            [new CreditNoteRedemptionRequest("CN-001", 10m)]) with
        {
            CreditNoteAppliedAmount = 50m,
        };
        var dto = CreateDto();
        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _controller.RecordSale(request, CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(dto, createdResult.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<SaleDto>>(
            Arg.Is<RecordSaleCommand>(c =>
                c.CreditNoteAppliedAmount == 50m
                && c.CreditNoteRedemptions!.Count == 1
                && c.CreditNoteRedemptions[0].Code == "CN-001"
                && c.CreditNoteRedemptions[0].Amount == 10m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncOfflineSales_WhenValid_ReturnsOkAndDispatchesCommand()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var request = CreateOfflineSyncRequest(batchId);
        var response = new OfflineSalesSyncResponseDto(
            [new OfflineSaleSyncResultDto(
                request.Sales[0].ClientSaleId,
                "created",
                Guid.NewGuid(),
                request.Sales[0].InvoiceNumber,
                [])]);

        _bus.InvokeAsync<ErrorOr<OfflineSalesSyncResponseDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _controller.SyncOfflineSales(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(response, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<OfflineSalesSyncResponseDto>>(
            Arg.Is<SyncOfflineSalesCommand>(c =>
                c.ActorUserId == userId
                && c.ShopId == shopId
                && c.DeviceId == request.DeviceId
                && c.Sales.Count == 1
                && c.Sales[0].InvoiceNumber == request.Sales[0].InvoiceNumber
                && c.Sales[0].Items[0].InventoryBatchId == batchId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncOfflineSales_WhenRequestBodyMissing_ReturnsBadRequest()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var result = await _controller.SyncOfflineSales(null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        await _bus.DidNotReceive().InvokeAsync<ErrorOr<OfflineSalesSyncResponseDto>>(
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SyncOfflineSales_WhenSalesMissing_ReturnsBadRequest()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));
        var request = CreateOfflineSyncRequest() with { Sales = null! };

        var result = await _controller.SyncOfflineSales(request, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        await _bus.DidNotReceive().InvokeAsync<ErrorOr<OfflineSalesSyncResponseDto>>(
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSale_WhenItemNotFound_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Sale.ItemNotFound("BC-999"));

        var result = await _controller.RecordSale(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Fact]
    public async Task RecordSale_WhenInsufficientStock_ReturnsBadRequest()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Sale.InsufficientStock("BC-001", "B-01"));

        var result = await _controller.RecordSale(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetSales_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.GetSales(cancellationToken: CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetSales_WhenActiveShopMissing_ReturnsProblemResult()
    {
        var userId = Guid.NewGuid();
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()));

        var result = await _controller.GetSales(cancellationToken: CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetSales_WhenSuccessful_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        IReadOnlyList<SaleListItemDto> sales = [];
        var response = new SalesHistoryResultDto(
            Items: sales,
            TotalCount: 0,
            PageNumber: 1,
            PageSize: 20,
            Summary: new SalesHistorySummaryDto(0m, 0, 0m));
        _bus.InvokeAsync<ErrorOr<SalesHistoryResultDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SalesHistoryResultDto>>(response));

        var result = await _controller.GetSales(cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(response, ok.Value);
    }

    [Fact]
    public async Task SearchSellables_WhenSuccessful_ReturnsOkAndDispatchesQuery()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var searchTerm = "rice";
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var sellables = CreateSellables();
        _bus.InvokeAsync<ErrorOr<IReadOnlyList<SellableDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<IReadOnlyList<SellableDto>>>(sellables.ToList()));

        var result = await _controller.SearchSellables(searchTerm, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(sellables, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<IReadOnlyList<SellableDto>>>(
            Arg.Is<SearchSellablesQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.SearchTerm == searchTerm
                && !q.IsBarcodeLookup),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchSellables_WhenBarcodeProvided_UsesBarcodeMode()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var barcode = $"QR|01|{Guid.NewGuid():N}|TRACE|{Guid.NewGuid():N}|PAYLOAD|{new string('B', 24)}";
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var sellables = CreateSellables();
        _bus.InvokeAsync<ErrorOr<IReadOnlyList<SellableDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<IReadOnlyList<SellableDto>>>(sellables.ToList()));

        var result = await _controller.SearchSellables(null, barcode, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(sellables, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<IReadOnlyList<SellableDto>>>(
            Arg.Is<SearchSellablesQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.SearchTerm == barcode
                && q.IsBarcodeLookup),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchSellables_WhenSearchTermAndBarcodeProvided_PrioritizesSearchMode()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var searchTerm = "apple";
        var barcode = $"QR|01|{Guid.NewGuid():N}|TRACE|{Guid.NewGuid():N}|PAYLOAD|{new string('B', 24)}";
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var sellables = CreateSellables();
        _bus.InvokeAsync<ErrorOr<IReadOnlyList<SellableDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<IReadOnlyList<SellableDto>>>(sellables.ToList()));

        var result = await _controller.SearchSellables(searchTerm, barcode, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(sellables, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<IReadOnlyList<SellableDto>>>(
            Arg.Is<SearchSellablesQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.SearchTerm == searchTerm
                && !q.IsBarcodeLookup),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchSellables_WhenSearchTermMissing_ReturnsBadRequest()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var result = await _controller.SearchSellables("   ", null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        await _bus.DidNotReceive().InvokeAsync<ErrorOr<IReadOnlyList<SellableDto>>>(
            Arg.Any<object>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSaleDetail_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.GetSaleDetail(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetSaleDetail_WhenSuccessful_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var sale = new SaleDto(saleId, "INV-001", null, "Ravi Kumar", "+919876543210", PaymentMethod.Cash, DateTimeOffset.UtcNow, 500m, 0m, 500m, 0m, 500m, 90m, 0m, [], []);
        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleDto>>(sale));

        var result = await _controller.GetSaleDetail(saleId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(sale, ok.Value);
    }

    [Fact]
    public async Task GetSaleByReturnNumber_WhenSuccessful_ReturnsOriginalSaleDetail()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var sale = new SaleDto(saleId, "INV-001", null, "Ravi Kumar", "+919876543210", PaymentMethod.Cash, DateTimeOffset.UtcNow, 500m, 0m, 500m, 0m, 500m, 90m, 0m, [], []);
        _bus.InvokeAsync<ErrorOr<Guid>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Guid>>(saleId));
        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleDto>>(sale));

        var result = await _controller.GetSaleByReturnNumber("RET-001", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(sale, ok.Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<Guid>>(
            Arg.Is<GetSaleByReturnNumberQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.ReturnNumber == "RET-001"),
            Arg.Any<CancellationToken>());
        await _bus.Received(1).InvokeAsync<ErrorOr<SaleDto>>(
            Arg.Is<GetSaleDetailQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.SaleId == saleId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewSaleReturn_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.PreviewSaleReturn(
            Guid.NewGuid(),
            new PreviewSaleReturnRequest(null, null, []),
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task PreviewSaleReturn_WhenActiveShopMissing_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        var result = await _controller.PreviewSaleReturn(
            Guid.NewGuid(),
            new PreviewSaleReturnRequest(null, null, []),
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task PreviewSaleReturn_WhenSuccessful_ReturnsOkAndDispatchesQuery()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var saleItemId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var dto = new SaleReturnPreviewDto(
            saleId,
            HasFinancialAccess: true,
            [],
            new SaleReturnPreviewFinancialDto(100m, 25m, 75m, 90m, 10m, 25m, 0m),
            []);
        _bus.InvokeAsync<ErrorOr<SaleReturnPreviewDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleReturnPreviewDto>>(dto));

        var result = await _controller.PreviewSaleReturn(
            saleId,
            new PreviewSaleReturnRequest(
                DueReductionOverrideAmount: 25m,
                DueOverrideReason: "Customer request",
                [new PreviewSaleReturnItemRequest(saleItemId, 1m, SaleReturnCondition.Restockable, 100m, "Sealed")]),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<SaleReturnPreviewDto>>(
            Arg.Is<PreviewSaleReturnQuery>(q =>
                q.ActorUserId == userId
                && q.ShopId == shopId
                && q.SaleId == saleId
                && q.DueReductionOverrideAmount == 25m
                && q.Items.Count == 1
                && q.Items[0].SaleItemId == saleItemId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewSale_WhenSuccessful_ReturnsOkAndDispatchesQuery()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var dto = new SalePreviewDto(
            TotalAmount: 100m,
            TotalTaxableAmount: 90m,
            TotalTaxAmount: 10m,
            TotalDiscountAmount: 0m,
            SaleLevelEligibleSubtotal: 90m,
            ConfiguredSaleRule: null,
            Lines: [],
            Infos: [],
            Warnings: []);

        _bus.InvokeAsync<ErrorOr<SalePreviewDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SalePreviewDto>>(dto));

        var request = new PreviewSaleRequest(
            new InstantDiscountRequest(InstantDiscountType.None, 0m),
            [
                new PreviewSaleItemRequest(
                    batchId,
                    "BC-001",
                    "B-01",
                    "Rice",
                    1m,
                    80m,
                    100m,
                    120m,
                    18m,
                    false,
                    new InstantDiscountRequest(InstantDiscountType.None, 0m),
                    "line-1"),
            ]);

        var result = await _controller.PreviewSale(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<SalePreviewDto>>(
            Arg.Is<PreviewSaleQuery>(q =>
                q.ActorUserId == userId
                && q.ShopId == shopId
                && q.SaleDiscount.Type == InstantDiscountType.None
                && q.Items.Count == 1
                && q.Items[0].InventoryBatchId == batchId
                && q.Items[0].ClientLineKey == "line-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordSaleReturn_WhenUserMissing_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.RecordSaleReturn(
            Guid.NewGuid(),
            new RecordSaleReturnRequest(ReturnPayoutDestination.Refund, null, null, null, []),
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task RecordSaleReturn_WhenSuccessful_ReturnsRefreshedSaleDetail()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var saleItemId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var sale = new SaleDto(
            saleId,
            "INV-001",
            null,
            "Ravi Kumar",
            "+919876543210",
            PaymentMethod.Cash,
            DateTimeOffset.UtcNow,
            500m,
            0m,
            500m,
            0m,
            500m,
            45m,
            0m,
            [],
            [])
        {
            Returns = [
                new SaleReturnDto(
                    Guid.NewGuid(),
                    "RET-20260505-ABC123EF",
                    DateTimeOffset.UtcNow,
                    userId,
                    null,
                    100m,
                    0m,
                    100m,
                    ReturnPayoutDestination.Refund,
                    90m,
                    10m,
                    [])
            ],
        };

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));
        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleDto>>(sale));

        var result = await _controller.RecordSaleReturn(
            saleId,
            new RecordSaleReturnRequest(
                ReturnPayoutDestination.Refund,
                DueReductionOverrideAmount: null,
                DueOverrideReason: null,
                Notes: "Customer returned sealed item",
                [new RecordSaleReturnItemRequest(saleItemId, 1m, SaleReturnCondition.Restockable, 100m, "Sealed")]),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(sale, ok.Value);
        await _bus.Received(1).InvokeAsync<ErrorOr<Success>>(
            Arg.Is<RecordSaleReturnCommand>(c =>
                c.ActorUserId == userId
                && c.ShopId == shopId
                && c.SaleId == saleId
                && c.PayoutDestination == ReturnPayoutDestination.Refund
                && c.Items.Count == 1
                && c.Items[0].SaleItemId == saleItemId),
            Arg.Any<CancellationToken>());
        await _bus.Received(1).InvokeAsync<ErrorOr<SaleDto>>(
            Arg.Is<GetSaleDetailQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.SaleId == saleId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task VoidSaleReturn_WhenSuccessful_ReturnsNoContentAndDispatchesCommand()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleReturnId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));

        var result = await _controller.VoidSaleReturn(
            saleReturnId,
            new VoidSaleReturnRequest("Duplicate return"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        await _bus.Received(1).InvokeAsync<ErrorOr<Success>>(
            Arg.Is<VoidSaleReturnCommand>(c =>
                c.ActorUserId == userId
                && c.ShopId == shopId
                && c.SaleReturnId == saleReturnId
                && c.Reason == "Duplicate return"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.UPI)]
    [InlineData(PaymentMethod.Card)]
    public async Task RecordSaleReturn_WithLegacyPayoutMethod_MapsToRefund(PaymentMethod legacyMethod)
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var sale = new SaleDto(
            saleId, "INV-001", null, "Ravi Kumar", null,
            PaymentMethod.Cash, DateTimeOffset.UtcNow,
            500m, 0m, 500m, 0m, 500m, 0m, 0m, [], []);

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<Success>>(Result.Success));
        _bus.InvokeAsync<ErrorOr<SaleDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<SaleDto>>(sale));

        var result = await _controller.RecordSaleReturn(
            saleId,
            new RecordSaleReturnRequest(
                PayoutDestination: null,
                DueReductionOverrideAmount: null,
                DueOverrideReason: null,
                Notes: null,
                Items: [],
                PayoutMethod: legacyMethod),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        await _bus.Received(1).InvokeAsync<ErrorOr<Success>>(
            Arg.Is<RecordSaleReturnCommand>(c =>
                c.PayoutDestination == ReturnPayoutDestination.Refund),
            Arg.Any<CancellationToken>());
    }
}
