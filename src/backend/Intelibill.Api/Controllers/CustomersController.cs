using ErrorOr;
using Intelibill.Api.Extensions;
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
public sealed class CustomersController : AuthenticatedControllerBase
{
    public CustomersController(IMessageBus bus) : base(bus)
    {
    }

    [HttpGet]
    public async Task<IActionResult> GetCustomers(CancellationToken cancellationToken)
    {
        var shop = CheckShop();
        if (shop is not null) return shop;

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<CustomerDto>>>(
            new GetCustomersQuery(ActiveShopId!.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    public async Task<IActionResult> AddCustomer([FromBody] AddCustomerRequest request, CancellationToken cancellationToken)
    {
        var shop = CheckShop();
        if (shop is not null) return shop;

        var result = await Bus.InvokeAsync<ErrorOr<CustomerDto>>(
            new AddCustomerCommand(
                ActiveShopId!.Value,
                request.Name,
                request.PhoneNumber,
                request.Address,
                request.IsActive,
                request.CreditLimit),
            cancellationToken);

        return result.ToActionResult(customer => CreatedAtAction(nameof(GetCustomers), customer));
    }

    [HttpPut("{customerId:guid}")]
    public async Task<IActionResult> EditCustomer(Guid customerId, [FromBody] EditCustomerRequest request, CancellationToken cancellationToken)
    {
        var shop = CheckShop();
        if (shop is not null) return shop;

        var result = await Bus.InvokeAsync<ErrorOr<CustomerDto>>(
            new EditCustomerCommand(
                ActiveShopId!.Value,
                customerId,
                request.Name,
                request.PhoneNumber,
                request.Address,
                request.IsActive,
                request.CreditLimit),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{customerId:guid}/account")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> GetCustomerAccount(Guid customerId, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<CustomerAccountDto>>(
            new GetCustomerAccountQuery(UserId!.Value, ActiveShopId!.Value, customerId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("{customerId:guid}/payments")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> RecordPayment(Guid customerId, [FromBody] RecordCustomerPaymentRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<CustomerLedgerEntryDto>>(
            new RecordCustomerPaymentCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                customerId,
                request.Amount,
                request.PaymentDate,
                request.Notes),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}

public sealed record AddCustomerRequest(
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive,
    decimal CreditLimit = 0m);

public sealed record EditCustomerRequest(
    string Name,
    string PhoneNumber,
    string? Address,
    bool IsActive,
    decimal CreditLimit);

public sealed record RecordCustomerPaymentRequest(
    decimal Amount,
    DateOnly PaymentDate,
    string? Notes);
