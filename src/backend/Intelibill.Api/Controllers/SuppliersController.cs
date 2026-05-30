using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Suppliers.Commands.AddSupplier;
using Intelibill.Application.Features.Suppliers.Commands.DeleteSupplier;
using Intelibill.Application.Features.Suppliers.Commands.EditSupplier;
using Intelibill.Application.Features.SupplierLedger.Commands.MakeSupplierPayment;
using Intelibill.Application.Features.Suppliers.DTOs;
using Intelibill.Application.Features.Suppliers.Queries.GetSuppliers;
using Intelibill.Application.Features.SupplierLedger.DTOs;
using Intelibill.Application.Features.SupplierLedger.Queries.GetSupplierEntries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize]
public sealed class SuppliersController : AuthenticatedControllerBase
{
    public SuppliersController(IMessageBus bus) : base(bus)
    {
    }

    [HttpGet]
    public async Task<IActionResult> GetSuppliers([FromQuery(Name = "include_system")] bool includeSystem = true, CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<SupplierDto>>>(
            new GetSuppliersQuery(UserId!.Value, ActiveShopId!.Value, includeSystem),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{supplierId:guid}/ledger")]
    public async Task<IActionResult> GetSupplierLedger(Guid supplierId, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<SupplierLedgerEntryDto>>>(
            new GetSupplierEntriesQuery(UserId!.Value, ActiveShopId!.Value, supplierId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> AddSupplier([FromBody] AddSupplierRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<SupplierDto>>(
            new AddSupplierCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                request.Name,
                request.ContactPersonName,
                request.ContactPersonPhone,
                request.Address,
                request.City,
                request.State,
                request.Pin,
                request.IsActive,
                request.IsPreferred),
            cancellationToken);

        return result.ToActionResult(supplier => CreatedAtAction(nameof(GetSuppliers), supplier));
    }

    [HttpPut("{supplierId:guid}")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> EditSupplier(Guid supplierId, [FromBody] EditSupplierRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<SupplierDto>>(
            new EditSupplierCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                supplierId,
                request.Name,
                request.ContactPersonName,
                request.ContactPersonPhone,
                request.Address,
                request.City,
                request.State,
                request.Pin,
                request.IsActive,
                request.IsPreferred),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpDelete("{supplierId:guid}")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> DeleteSupplier(Guid supplierId, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<Success>>(
            new DeleteSupplierCommand(UserId!.Value, ActiveShopId!.Value, supplierId),
            cancellationToken);

        if (result.IsError)
            return result.Errors.ToList().ToProblemResult();

        return NoContent();
    }

    [HttpPost("{supplierId:guid}/payments")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> MakePayment(Guid supplierId, [FromBody] MakePaymentRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<SupplierLedgerEntryDto>>(
            new MakeSupplierPaymentCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                supplierId,
                request.Amount,
                request.PaymentDate,
                request.Notes),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}

public sealed record AddSupplierRequest(
    string Name,
    string? ContactPersonName,
    string? ContactPersonPhone,
    string Address,
    string City,
    string State,
    string Pin,
    bool IsActive,
    bool IsPreferred);

public sealed record EditSupplierRequest(
    string Name,
    string? ContactPersonName,
    string? ContactPersonPhone,
    string Address,
    string City,
    string State,
    string Pin,
    bool IsActive,
    bool IsPreferred);

public sealed record MakePaymentRequest(
    decimal Amount,
    DateOnly PaymentDate,
    string? Notes);
