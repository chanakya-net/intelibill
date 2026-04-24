using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Extensions;
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
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await bus.InvokeAsync<ErrorOr<IReadOnlyList<BankAccountDto>>>(
            new GetBankAccountsQuery(userId.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> AddBankAccount([FromBody] AddBankAccountRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await bus.InvokeAsync<ErrorOr<BankAccountDto>>(
            new AddBankAccountCommand(
                userId.Value,
                request.BankName,
                request.AccountNumber,
                request.AccountType,
                request.IfscCode,
                request.AccountHolderName),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    private Guid? GetCurrentUserId()
    {
        var sub = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(sub, out var userId) ? userId : null;
    }
}

public sealed record AddBankAccountRequest(
    string? BankName,
    string? AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName);
