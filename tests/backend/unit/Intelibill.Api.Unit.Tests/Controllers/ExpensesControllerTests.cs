using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Controllers;
using Intelibill.Application.Features.Expenses.Commands.CorrectExpense;
using Intelibill.Application.Features.Expenses.Commands.RecordExpense;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Application.Features.Expenses.Queries.GetExpenseCategories;
using Intelibill.Application.Features.Expenses.Queries.GetExpenseDetail;
using Intelibill.Application.Features.Expenses.Queries.GetExpenses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Wolverine;

namespace Intelibill.Api.Unit.Tests.Controllers;

public class ExpensesControllerTests
{
    private readonly IMessageBus _bus = Substitute.For<IMessageBus>();
    private readonly ExpensesController _controller;

    public ExpensesControllerTests()
    {
        _controller = new ExpensesController(_bus);
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

    private static RecordExpenseRequest CreateRecordRequest() =>
        new("Supplies", 150.00m, "Stationery Store", "Monthly office supplies", new DateOnly(2026, 4, 23));

    private static CorrectExpenseRequest CreateCorrectRequest() =>
        new("Supplies", 175.00m, "Stationery Store", "Corrected amount", new DateOnly(2026, 4, 23));

    private static ExpenseDto CreateExpenseDto() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Supplies",
            150.00m,
            "Stationery Store",
            "Monthly office supplies",
            new DateOnly(2026, 4, 23),
            Guid.NewGuid(),
            false,
            null,
            DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetCategories_WhenSuccessful_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        IReadOnlyList<ExpenseCategoryDto> categories =
        [
            new ExpenseCategoryDto(Guid.NewGuid(), "Rent"),
            new ExpenseCategoryDto(Guid.NewGuid(), "Utilities"),
        ];

        _bus.InvokeAsync<ErrorOr<IReadOnlyList<ExpenseCategoryDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<IReadOnlyList<ExpenseCategoryDto>>>(categories.ToList()));

        var result = await _controller.GetCategories(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(categories, ok.Value);
    }

    [Fact]
    public async Task RecordExpense_WhenValid_ReturnsCreatedAtAction()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var dto = CreateExpenseDto();
        _bus.InvokeAsync<ErrorOr<ExpenseDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(dto);

        var result = await _controller.RecordExpense(CreateRecordRequest(), CancellationToken.None);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        Assert.Equal(dto, createdResult.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<ExpenseDto>>(
            Arg.Is<RecordExpenseCommand>(c =>
                c.UserId == userId
                && c.ShopId == shopId
                && c.CategoryName == "Supplies"
                && c.Amount == 150.00m
                && c.PaidTo == "Stationery Store"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordExpense_WhenNoUserClaim_ReturnsUnauthorized()
    {
        SetUserClaims();

        var result = await _controller.RecordExpense(CreateRecordRequest(), CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetExpenses_WhenSuccessful_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var expenses = new PaginatedList<ExpenseListItemDto>(
            [new ExpenseListItemDto(Guid.NewGuid(), 150.00m, "Supplies", "Stationery Store", new DateOnly(2026, 4, 23), false)],
            1,
            1,
            20);

        _bus.InvokeAsync<ErrorOr<PaginatedList<ExpenseListItemDto>>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<PaginatedList<ExpenseListItemDto>>>(expenses));

        var result = await _controller.GetExpenses(null, 1, 20, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(expenses, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<PaginatedList<ExpenseListItemDto>>>(
            Arg.Is<GetExpensesQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.SearchTerm == null
                && q.PageNumber == 1
                && q.PageSize == 20),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetExpense_WhenSuccessful_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var dto = CreateExpenseDto();
        _bus.InvokeAsync<ErrorOr<ExpenseDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<ExpenseDto>>(dto));

        var result = await _controller.GetExpense(expenseId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<ExpenseDto>>(
            Arg.Is<GetExpenseDetailQuery>(q =>
                q.UserId == userId
                && q.ShopId == shopId
                && q.ExpenseId == expenseId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorrectExpense_WhenSuccessful_ReturnsOk()
    {
        var userId = Guid.NewGuid();
        var shopId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        SetUserClaims(
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim("active_shop_id", shopId.ToString()));

        var dto = CreateExpenseDto();
        _bus.InvokeAsync<ErrorOr<ExpenseDto>>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ErrorOr<ExpenseDto>>(dto));

        var result = await _controller.CorrectExpense(expenseId, CreateCorrectRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(dto, ok.Value);

        await _bus.Received(1).InvokeAsync<ErrorOr<ExpenseDto>>(
            Arg.Is<CorrectExpenseCommand>(c =>
                c.UserId == userId
                && c.ShopId == shopId
                && c.OriginalExpenseId == expenseId
                && c.CategoryName == "Supplies"
                && c.Amount == 175.00m
                && c.PaidTo == "Stationery Store"),
            Arg.Any<CancellationToken>());
    }
}
