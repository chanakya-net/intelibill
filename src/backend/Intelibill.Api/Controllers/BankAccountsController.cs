using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.BankAccounts.Commands.AddBankAccount;
using Intelibill.Application.Features.BankAccounts.DTOs;
using Intelibill.Application.Features.BankAccounts.Queries.GetBankAccounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/bank-accounts")]
[Authorize]
public sealed class BankAccountsController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetBankAccounts(CancellationToken cancellationToken)
    {
        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<IReadOnlyList<BankAccountDto>>>(
            new GetBankAccountsQuery(activeShopId.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> AddBankAccount([FromBody] AddBankAccountRequest request, CancellationToken cancellationToken)
    {
        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<BankAccountDto>>(
            new AddBankAccountCommand(
                activeShopId.Value,
                request.BankName,
                request.AccountNumber,
                request.AccountType,
                request.IfscCode,
                request.AccountHolderName),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    private Guid? GetCurrentActiveShopId()
    {
        var activeShopId = User.FindFirst("active_shop_id")?.Value;
        return Guid.TryParse(activeShopId, out var shopId) ? shopId : null;
    }
}

public sealed record AddBankAccountRequest(
    string? BankName,
    string? AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName);
