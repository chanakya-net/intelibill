using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Features.CreditNotes.DTOs;
using Intelibill.Application.Features.CreditNotes.Queries.GetCreditNoteByCode;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class CreditNotesControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly CreditNotesController _controller;

    public CreditNotesControllerTests()
    {
        _controller = new CreditNotesController(_bus);
    }

    [Fact]
    public void GetCreditNoteByCode_HasOwnerManagerOrStaffPolicy()
    {
        var method = typeof(CreditNotesController).GetMethod(nameof(CreditNotesController.GetCreditNoteByCode));
        var attr = method?
            .GetCustomAttributes<AuthorizeAttribute>()
            .FirstOrDefault(a => a.Policy == "OwnerManagerOrStaff");

        Assert.NotNull(attr);
    }

    [Fact]
    public async Task GetCreditNoteByCode_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.GetCreditNoteByCode("CN-001", CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetCreditNoteByCode_WhenValid_ReturnsOkAndDispatchesQuery()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var dto = new CreditNoteDto(
            Guid.NewGuid(),
            "CN-001",
            CreditNoteStatus.Active,
            100m,
            75m,
            null,
            false,
            Guid.NewGuid(),
            "Return reason",
            null);

        _bus.InvokeAsync<ErrorOr<CreditNoteDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _controller.GetCreditNoteByCode(" CN-001 ", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<CreditNoteDto>>(
            Arg.Is<GetCreditNoteByCodeQuery>(q =>
                q.UserId == userId &&
                q.ActiveShopId == shopId &&
                q.Code == " CN-001 "),
            Arg.Any<CancellationToken>());
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
