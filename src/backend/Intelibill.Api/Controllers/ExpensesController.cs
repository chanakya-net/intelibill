using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Expenses.Commands.CorrectExpense;
using Intelibill.Application.Features.Expenses.Commands.RecordExpense;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Application.Features.Expenses.Queries.GetExpenseCategories;
using Intelibill.Application.Features.Expenses.Queries.GetExpenseDetail;
using Intelibill.Application.Features.Expenses.Queries.GetExpenses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/expenses")]
[Authorize]
public sealed class ExpensesController(IMessageBus bus) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> RecordExpense([FromBody] RecordExpenseRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<ExpenseDto>>(
            new RecordExpenseCommand(
                userId.Value,
                activeShopId.Value,
                request.CategoryName,
                request.Amount,
                request.PaidTo,
                request.Description,
                request.ExpenseDate),
            cancellationToken);

        return result.ToActionResult(sale => CreatedAtAction(nameof(GetExpense), new { id = sale.Id }, sale));
    }

    [HttpPost("{id:guid}/correct")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> CorrectExpense(Guid id, [FromBody] CorrectExpenseRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<ExpenseDto>>(
            new CorrectExpenseCommand(
                userId.Value,
                activeShopId.Value,
                id,
                request.CategoryName,
                request.Amount,
                request.PaidTo,
                request.Description,
                request.ExpenseDate),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet]
    public async Task<IActionResult> GetExpenses([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<PaginatedList<ExpenseListItemDto>>>(
            new GetExpensesQuery(userId.Value, activeShopId.Value, search, page, pageSize),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetExpense(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<ExpenseDto>>(
            new GetExpenseDetailQuery(userId.Value, activeShopId.Value, id),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<IReadOnlyList<ExpenseCategoryDto>>>(
            new GetExpenseCategoriesQuery(userId.Value, activeShopId.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }

    private Guid? GetCurrentActiveShopId()
    {
        var activeShopId = User.FindFirst("active_shop_id")?.Value;
        return Guid.TryParse(activeShopId, out var shopId) ? shopId : null;
    }
}

public sealed record RecordExpenseRequest(
    string CategoryName,
    decimal Amount,
    string PaidTo,
    string? Description,
    DateOnly ExpenseDate);

public sealed record CorrectExpenseRequest(
    string CategoryName,
    decimal Amount,
    string PaidTo,
    string? Description,
    DateOnly ExpenseDate);
