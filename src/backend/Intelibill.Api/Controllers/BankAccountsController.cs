using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.BankAccounts.Commands.AddBankAccount;
using Intelibill.Application.Features.BankAccounts.Commands.DeleteBankAccount;
using Intelibill.Application.Features.BankAccounts.Commands.UpdateBankAccount;
using Intelibill.Application.Features.BankAccounts.DTOs;
using Intelibill.Application.Features.BankAccounts.Queries.GetBankAccounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/bank-accounts")]
[Authorize]
public sealed class BankAccountsController : AuthenticatedControllerBase
{
    public BankAccountsController(IMessageBus bus) : base(bus)
    {
    }

    [HttpGet]
    public async Task<IActionResult> GetBankAccounts(CancellationToken cancellationToken)
    {
        var shop = CheckShop();
        if (shop is not null) return shop;

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<BankAccountDto>>>(
            new GetBankAccountsQuery(ActiveShopId!.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> AddBankAccount([FromBody] AddBankAccountRequest request, CancellationToken cancellationToken)
    {
        var shop = CheckShop();
        if (shop is not null) return shop;

        var result = await Bus.InvokeAsync<ErrorOr<BankAccountDto>>(
            new AddBankAccountCommand(
                ActiveShopId!.Value,
                request.BankName,
                request.AccountNumber,
                request.AccountType,
                request.IfscCode,
                request.AccountHolderName),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> UpdateBankAccount(Guid id, [FromBody] UpdateBankAccountRequest request, CancellationToken cancellationToken)
    {
        var shop = CheckShop();
        if (shop is not null) return shop;

        var result = await Bus.InvokeAsync<ErrorOr<BankAccountDto>>(
            new UpdateBankAccountCommand(
                id,
                ActiveShopId!.Value,
                request.BankName,
                request.AccountNumber,
                request.AccountType,
                request.IfscCode,
                request.AccountHolderName),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> DeleteBankAccount(Guid id, CancellationToken cancellationToken)
    {
        var shop = CheckShop();
        if (shop is not null) return shop;

        var result = await Bus.InvokeAsync<ErrorOr<Deleted>>(
            new DeleteBankAccountCommand(id, ActiveShopId!.Value),
            cancellationToken);

        return result.ToActionResult(_ => NoContent());
    }
}

public sealed record AddBankAccountRequest(
    string? BankName,
    string? AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName);

public sealed record UpdateBankAccountRequest(
    string? BankName,
    string? AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName);
