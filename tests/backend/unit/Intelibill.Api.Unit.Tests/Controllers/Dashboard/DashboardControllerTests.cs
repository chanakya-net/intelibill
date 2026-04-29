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

namespace Intelibill.Api.Unit.Tests.Controllers.Dashboard;

public class DashboardControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly DashboardController _controller;

    public DashboardControllerTests()
    {
        _controller = new DashboardController(_bus);
    }

    private void SetUserClaims(params Claim[] claims)
    {
        var identity = claims.Length == 0
            ? new ClaimsIdentity()
            : new ClaimsIdentity(claims, "Test");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
    }

    private static DashboardDto MakeDashboardDto() =>
        new(
            GeneratedAt: DateTimeOffset.UtcNow,
            ReportingDay: DateOnly.FromDateTime(DateTime.UtcNow),
            SalesCount: 3,
            HasNoSalesActivity: false,
            SalesBooked: 300m,
            CashCollected: 250m,
            ProfitBeforeTax: 60m,
            ProfitAfterTax: 90m,
            ExpenseRecorded: 50m,
            ExpenseCorrection: 0m,
            NetExpense: 50m,
            CreditSalesAmount: 50m,
            CreditSalesPercentage: 0.17m,
            PaymentMix: new(250m, 0m, 0m, 50m),
            CreditShareWarning: false,
            RunningLowStockCount: 1,
            CriticalStockCount: 0,
            RankedShortageList: [],
            HighestDueCustomer: null,
            TopFiveDueCustomers: [],
            Alerts: []);

    [Fact]
    public async Task GetDashboard_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.GetDashboard(null, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetDashboard_WhenNoActiveShopClaim_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        var result = await _controller.GetDashboard(null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_WhenValid_ReturnsOkWithDashboard()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var dto = MakeDashboardDto();
        _bus.InvokeAsync<ErrorOr<DashboardDto>>(
                Arg.Is<GetDashboardQuery>(q => q.UserId == userId && q.ShopId == shopId),
                Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _controller.GetDashboard(null, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var returned = Assert.IsType<DashboardDto>(okResult.Value);
        Assert.Equal(dto.SalesCount, returned.SalesCount);
    }

    [Fact]
    public async Task GetDashboard_WhenHandlerReturnsError_ReturnsProblemResult()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<DashboardDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Errors.Shop.ShopNotFound);

        var result = await _controller.GetDashboard(null, CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }
}
