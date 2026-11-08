using Intelibill.Application.Features.Tills;
using Intelibill.Domain.ValueObjects;
using Intelibill.Domain.Enums;
using ErrorOr;
using Intelibill.Api.Extensions;
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
public sealed class ExpensesController : AuthenticatedControllerBase
{
    public ExpensesController(IMessageBus bus) : base(bus)
    {
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> RecordExpense([FromBody] RecordExpenseRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<ExpenseDto>>(
            new RecordExpenseCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                request.CategoryName,
                request.Amount,
                request.PaidTo,
                request.Description,
                request.ExpenseDate, request.Treatment, request.OperationKey, request.Till, request.TenderLine, request.SettlementEvidence),
            cancellationToken);

        return result.ToActionResult(sale => CreatedAtAction(nameof(GetExpense), new { id = sale.Id }, sale));
    }

    [HttpPost("{id:guid}/correct")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> CorrectExpense(Guid id, [FromBody] CorrectExpenseRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<ExpenseDto>>(
            new CorrectExpenseCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                id,
                request.CategoryName,
                request.Amount,
                request.PaidTo,
                request.Description,
                request.ExpenseDate, request.Treatment, request.OperationKey, request.Till, request.SettlementEvidence),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet]
    public async Task<IActionResult> GetExpenses([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null, CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var query = new GetExpensesQuery(UserId!.Value, ActiveShopId!.Value, search, page, pageSize, status);
        var validation = await new GetExpensesQueryValidator().ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.Errors.Select(failure => Error.Validation(failure.ErrorCode, failure.ErrorMessage))
                .ToList().ToProblemResult();
        }

        var result = await Bus.InvokeAsync<ErrorOr<PaginatedList<ExpenseListItemDto>>>(
            query,
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetExpense(Guid id, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<ExpenseDto>>(
            new GetExpenseDetailQuery(UserId!.Value, ActiveShopId!.Value, id),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<ExpenseCategoryDto>>>(
            new GetExpenseCategoriesQuery(UserId!.Value, ActiveShopId!.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}

public sealed record RecordExpenseRequest(
    string CategoryName,
    decimal Amount,
    string PaidTo,
    string? Description,
    DateOnly ExpenseDate,
    ExpenseTreatment Treatment = ExpenseTreatment.Unclassified,
    string? OperationKey = null, TillPostingContext? Till = null, TenderLine? TenderLine = null, TillSettlementEvidence? SettlementEvidence = null);

public sealed record CorrectExpenseRequest(
    string CategoryName,
    decimal Amount,
    string PaidTo,
    string? Description,
    DateOnly ExpenseDate,
    ExpenseTreatment? Treatment = null,
    string? OperationKey = null, TillPostingContext? Till = null, TillSettlementEvidence? SettlementEvidence = null);
