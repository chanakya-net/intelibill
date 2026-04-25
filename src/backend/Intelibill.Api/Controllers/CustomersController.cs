using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Customers.Commands.AddCustomer;
using Intelibill.Application.Features.Customers.Commands.EditCustomer;
using Intelibill.Application.Features.Customers.Commands.RecordCustomerPayment;
using Intelibill.Application.Features.Customers.DTOs;
using Intelibill.Application.Features.Customers.Queries.GetCustomerAccount;
using Intelibill.Application.Features.Customers.Queries.GetCustomers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize]
public sealed class CustomersController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCustomers(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<IReadOnlyList<CustomerDto>>>(
            new GetCustomersQuery(userId.Value, activeShopId.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    public async Task<IActionResult> AddCustomer([FromBody] AddCustomerRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await bus.InvokeAsync<ErrorOr<CustomerDto>>(
            new AddCustomerCommand(
                userId.Value,
                request.Name,
                request.PhoneNumber,
                request.Address,
                request.IsActive),
            cancellationToken);

        return result.ToActionResult(customer => CreatedAtAction(nameof(GetCustomers), customer));
    }

    [HttpPut("{customerId:guid}")]
    public async Task<IActionResult> EditCustomer(Guid customerId, [FromBody] EditCustomerRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var result = await bus.InvokeAsync<ErrorOr<CustomerDto>>(
            new EditCustomerCommand(
                userId.Value,
                customerId,
                request.Name,
                request.PhoneNumber,
                request.Address,
                request.IsActive),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{customerId:guid}/account")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> GetCustomerAccount(Guid customerId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<CustomerAccountDto>>(
            new GetCustomerAccountQuery(userId.Value, activeShopId.Value, customerId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("{customerId:guid}/payments")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> RecordPayment(Guid customerId, [FromBody] RecordCustomerPaymentRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<CustomerLedgerEntryDto>>(
            new RecordCustomerPaymentCommand(
                userId.Value,
                activeShopId.Value,
                customerId,
                request.Amount,
                request.PaymentDate,
                request.Notes),
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

public sealed record AddCustomerRequest(
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive);

public sealed record EditCustomerRequest(
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive);

public sealed record RecordCustomerPaymentRequest(
    decimal Amount,
    DateOnly PaymentDate,
    string? Notes);
