using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Suppliers.Commands.AddSupplier;
using Intelibill.Application.Features.Suppliers.Commands.EditSupplier;
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
public sealed class SuppliersController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetSuppliers(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<IReadOnlyList<SupplierDto>>>(
            new GetSuppliersQuery(userId.Value, activeShopId.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{supplierId:guid}/ledger")]
    public async Task<IActionResult> GetSupplierLedger(Guid supplierId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<IReadOnlyList<SupplierLedgerEntryDto>>>(
            new GetSupplierEntriesQuery(userId.Value, activeShopId.Value, supplierId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> AddSupplier([FromBody] AddSupplierRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<SupplierDto>>(
            new AddSupplierCommand(
                userId.Value,
                activeShopId.Value,
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
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<SupplierDto>>(
            new EditSupplierCommand(
                userId.Value,
                activeShopId.Value,
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
