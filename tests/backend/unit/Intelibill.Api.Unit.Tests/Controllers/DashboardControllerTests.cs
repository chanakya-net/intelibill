using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Dashboard.DTOs;
using Intelibill.Application.Features.Dashboard.Queries.GetDashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public sealed class DashboardControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly DashboardController _controller;

    public DashboardControllerTests()
    {
        _controller = new DashboardController(_bus);
    }

    [Fact]
    public async Task GetDashboard_WhenOwner_InvokesQueryAndReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var dto = CreateDashboardDto();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()),
            new Claim("active_shop_role", "Owner"));
        ArrangeBusResponse(dto);

        var result = await _controller.GetDashboard(null, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(dto, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<DashboardDto>>(
            Arg.Is<GetDashboardQuery>(query =>
                query.UserId == userId
                && query.ShopId == shopId
                && query.From == null
                && query.To == null
                && query.Role == "Owner"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDashboard_WhenManager_InvokesQueryAndReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var dto = CreateDashboardDto();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()),
            new Claim("active_shop_role", "Manager"));
        ArrangeBusResponse(dto);

        var result = await _controller.GetDashboard(null, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(dto, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<DashboardDto>>(
            Arg.Is<GetDashboardQuery>(query =>
                query.UserId == userId
                && query.ShopId == shopId
                && query.From == null
                && query.To == null
                && query.Role == "Manager"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDashboard_WhenDatesProvided_PassesThemToQuery()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var from = new DateOnly(2026, 5, 1);
        var to = new DateOnly(2026, 5, 31);
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()),
            new Claim("active_shop_role", "Owner"));
        ArrangeBusResponse(CreateDashboardDto(from, to));

        await _controller.GetDashboard(from, to, CancellationToken.None);

        await _bus.Received(1).InvokeAsync<ErrorOr<DashboardDto>>(
            Arg.Is<GetDashboardQuery>(query => query.From == from && query.To == to),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDashboard_WhenNoActiveShop_ReturnsBadRequest()
    {
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim("active_shop_role", "Owner"));

        var result = await _controller.GetDashboard(null, null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_WhenHandlerReturnsValidationError_ReturnsBadRequest()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));
        _bus.InvokeAsync<ErrorOr<DashboardDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<DashboardDto>>(Errors.Dashboard.PartialDateRange));

        var result = await _controller.GetDashboard(new DateOnly(2026, 5, 1), null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    private static DashboardDto CreateDashboardDto(DateOnly? from = null, DateOnly? to = null)
    {
        var appliedTo = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var appliedFrom = from ?? appliedTo.AddDays(-29);

        return new DashboardDto(
            GeneratedAt: DateTimeOffset.UtcNow,
            StartDate: appliedFrom,
            EndDate: appliedTo,
            SalesCount: 0,
            SalesRevenue: 0m,
            HasNoSalesActivity: true,
            CustomerCreditDue: 0m,
            SalesBooked: 0m,
            NetSalesBooked: 0m,
            WastageCost: 0m,
            CashCollected: 0m,
            ProfitBeforeTax: 0m,
            ProfitAfterTax: 0m,
            ExpenseRecorded: 0m,
            ExpenseCorrection: 0m,
            NetExpense: 0m,
            CreditSalesAmount: 0m,
            CreditSalesPercentage: 0m,
            PaymentMix: new PaymentMixDto(0m, 0m, 0m, 0m),
            CreditShareWarning: false,
            RunningLowStockCount: 0,
            CriticalStockCount: 0,
            RankedShortageList: Array.Empty<StockShortageItemDto>(),
            HighestDueCustomer: new CustomerDueDto(Guid.Empty, string.Empty, 0m),
            TopFiveDueCustomers: Array.Empty<CustomerDueDto>(),
            Alerts: Array.Empty<DashboardAlertDto>(),
            SalesTrendSeries: Array.Empty<SalesTrendPointDto>(),
            ProfitTrendSeries: Array.Empty<ProfitTrendPointDto>(),
            PaymentMixTrendSeries: Array.Empty<PaymentMixTrendPointDto>(),
            PreviousPeriodSummary: new PreviousPeriodSummaryDto(
                StartDate: appliedFrom,
                EndDate: appliedTo,
                SalesCount: 0,
                SalesBooked: 0m,
                NetSalesBooked: 0m,
                ProfitAfterTax: 0m,
                NetExpense: 0m,
                CreditSalesPercentage: 0m),
            LatestSales: Array.Empty<DashboardLatestSaleDto>(),
            StockValue: 0m);
    }

    private void ArrangeBusResponse(ErrorOr<DashboardDto> response)
    {
        _bus.InvokeAsync<ErrorOr<DashboardDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
    }

    private void SetUserClaims(params Claim[] claims)
    {
        var identity = claims.Length == 0 ? new ClaimsIdentity() : new ClaimsIdentity(claims, "test");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }
}
