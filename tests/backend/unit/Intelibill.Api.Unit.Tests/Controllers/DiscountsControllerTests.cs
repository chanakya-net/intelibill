using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Features.Discounts.DTOs;
using Intelibill.Application.Features.Discounts.Queries.PreviewDiscountRule;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class DiscountsControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly DiscountsController _controller;

    public DiscountsControllerTests()
    {
        _controller = new DiscountsController(_bus);
    }

    [Fact]
    public void PreviewDiscountRule_HasOwnerOrManagerPolicy()
    {
        var method = typeof(DiscountsController)
            .GetMethod(nameof(DiscountsController.PreviewDiscountRule));
        var attr = method?
            .GetCustomAttributes<AuthorizeAttribute>()
            .FirstOrDefault(a => a.Policy == "OwnerOrManager");
        Assert.NotNull(attr);
    }

    [Theory]
    [InlineData(nameof(DiscountsController.GetDiscountRules))]
    [InlineData(nameof(DiscountsController.GetDiscountRule))]
    [InlineData(nameof(DiscountsController.CreateDiscountRule))]
    [InlineData(nameof(DiscountsController.ReplaceDiscountRule))]
    [InlineData(nameof(DiscountsController.DisableDiscountRule))]
    public void ManagementEndpoints_HaveOwnerOrManagerPolicy(string methodName)
    {
        var method = typeof(DiscountsController).GetMethod(methodName);
        Assert.NotNull(method);

        var attr = method!
            .GetCustomAttributes<AuthorizeAttribute>()
            .FirstOrDefault(a => a.Policy == "OwnerOrManager");
        Assert.NotNull(attr);
    }

    [Fact]
    public async Task PreviewDiscountRule_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.PreviewDiscountRule(CreateRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task PreviewDiscountRule_WhenNoActiveShopClaim_ReturnsBadRequest()
    {
        SetUserClaims(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        var result = await _controller.PreviewDiscountRule(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    [Fact]
    public async Task PreviewDiscountRule_WhenValid_ReturnsOkWithPreviewDto()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var preview = new DiscountRulePreviewDto(
            AffectedCount: 3,
            AffectedSample: [],
            BelowCostSample: [],
            SafeMaxPercentage: 20m,
            Errors: [],
            Infos: []);

        _bus.InvokeAsync<ErrorOr<DiscountRulePreviewDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(preview);

        var request = CreateRequest();
        var result = await _controller.PreviewDiscountRule(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(preview, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<DiscountRulePreviewDto>>(
            Arg.Is<PreviewDiscountRuleQuery>(q =>
                q.ActorUserId == userId
                && q.ShopId == shopId
                && q.RuleType == request.RuleType
                && q.Percentage == request.Percentage
                && q.BelowCostConfirmed == request.BelowCostConfirmed),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PreviewDiscountRule_WhenHandlerReturnsError_ReturnsProblemResult()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<DiscountRulePreviewDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Error.NotFound("User.NotFound", "User not found."));

        var result = await _controller.PreviewDiscountRule(CreateRequest(), CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    private static PreviewDiscountRuleRequest CreateRequest() =>
        new(
            RuleType: DiscountRuleType.SalePercentage,
            Percentage: 10m,
            ThresholdAmount: null,
            InventoryBatchId: null,
            StartsAt: null,
            EndsAt: null,
            BelowCostConfirmed: false);

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
