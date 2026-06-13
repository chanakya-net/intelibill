using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Features.CreditNotes.DTOs;
using Intelibill.Application.Features.CreditNotes.Commands.VoidCreditNote;
using Intelibill.Application.Features.CreditNotes.Queries.GetCreditNoteByCode;
using Intelibill.Application.Features.CreditNotes.Queries.GetCreditNotes;
using Intelibill.Application.Features.Expenses.DTOs;
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
    public void VoidCreditNote_HasOwnerOrManagerPolicy()
    {
        var method = typeof(CreditNotesController).GetMethod(nameof(CreditNotesController.VoidCreditNote));
        var attr = method?
            .GetCustomAttributes<AuthorizeAttribute>()
            .FirstOrDefault(a => a.Policy == "OwnerOrManager");

        Assert.NotNull(attr);
    }

    [Fact]
    public void GetCreditNotes_HasOwnerManagerOrStaffPolicy()
    {
        var method = typeof(CreditNotesController).GetMethod(nameof(CreditNotesController.GetCreditNotes));
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

    [Fact]
    public async Task VoidCreditNote_WhenValid_ReturnsNoContentAndDispatchesCommand()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        _bus.InvokeAsync<ErrorOr<Success>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success);

        var result = await _controller.VoidCreditNote(" CN-001 ", new VoidCreditNoteRequest("Issued in error"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        await _bus.Received(1).InvokeAsync<ErrorOr<Success>>(
            Arg.Is<VoidCreditNoteCommand>(c =>
                c.ActorUserId == userId
                && c.ActiveShopId == shopId
                && c.Code == " CN-001 "
                && c.Reason == "Issued in error"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCreditNotes_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.GetCreditNotes(null, null, 1, 20, CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetCreditNotes_WhenValid_ReturnsOkAndDispatchesQuery()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var page = new PaginatedList<CreditNoteListItemDto>([], 0, 1, 20);
        _bus.InvokeAsync<ErrorOr<PaginatedList<CreditNoteListItemDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<PaginatedList<CreditNoteListItemDto>>>(page));

        var result = await _controller.GetCreditNotes("CN-", CreditNoteStatus.Active, 2, 10, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(page, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<PaginatedList<CreditNoteListItemDto>>>(
            Arg.Is<GetCreditNotesQuery>(q =>
                q.UserId == userId &&
                q.ShopId == shopId &&
                q.SearchTerm == "CN-" &&
                q.Status == CreditNoteStatus.Active &&
                q.PageNumber == 2 &&
                q.PageSize == 10),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetCreditNotes_WhenNoFilters_PassesNullSearchAndStatus()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var page = new PaginatedList<CreditNoteListItemDto>([], 0, 1, 20);
        _bus.InvokeAsync<ErrorOr<PaginatedList<CreditNoteListItemDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<PaginatedList<CreditNoteListItemDto>>>(page));

        await _controller.GetCreditNotes(null, null, 1, 20, CancellationToken.None);

        await _bus.Received(1).InvokeAsync<ErrorOr<PaginatedList<CreditNoteListItemDto>>>(
            Arg.Is<GetCreditNotesQuery>(q =>
                q.SearchTerm == null &&
                q.Status == null &&
                q.PageNumber == 1 &&
                q.PageSize == 20),
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
