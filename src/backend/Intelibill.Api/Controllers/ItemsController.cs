using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Interfaces;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Items.Commands.AddItem;
using Intelibill.Application.Features.Items.Commands.UpdateItem;
using Intelibill.Application.Features.Items.DTOs;
using Intelibill.Application.Features.Items.Queries.GetItems;
using Intelibill.Application.Features.Items.Queries.GetProductDetails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/items")]
[Authorize]
public sealed class ItemsController(
    IMessageBus bus,
    IItemCatalogStreamingService itemCatalogStreamingService) : ControllerBase
{
    [HttpGet("stream")]
    public async Task StreamItems(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            Response.StatusCode = 401;
            return;
        }

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
        {
            Response.StatusCode = 400;
            return;
        }

        var validation = await itemCatalogStreamingService.ValidateAccessAsync(
            userId.Value,
            activeShopId.Value,
            cancellationToken);

        if (validation.IsError)
        {
            Response.StatusCode = validation.FirstError.Type == ErrorType.NotFound ? 401 : 403;
            return;
        }

        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers.CacheControl = "no-store";

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var count = 0;

        await foreach (var item in itemCatalogStreamingService.StreamByShopAsync(activeShopId.Value, cancellationToken))
        {
            var line = JsonSerializer.Serialize(item, jsonOptions) + "\n";
            await Response.WriteAsync(line, cancellationToken);
            count++;
            if (count % 50 == 0)
                await Response.Body.FlushAsync(cancellationToken);
        }

        await Response.Body.FlushAsync(cancellationToken);
    }

    [HttpGet]
    public async Task<IActionResult> GetItems(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<IReadOnlyList<ItemDto>>>(
            new GetItemsQuery(userId.Value, activeShopId.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("details")]
    public async Task<IActionResult> GetProductDetails(
        [FromQuery] string? name,
        [FromQuery] string? barcode,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(barcode))
            return BadRequest("Either name or barcode must be provided");

        var result = await bus.InvokeAsync<ErrorOr<ProductDetailsDto>>(
            new GetProductDetailsByNameOrBarcodeQuery(userId.Value, activeShopId.Value, name, barcode),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> AddItem([FromBody] AddItemRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<ItemDto>>(
            new AddItemCommand(
                userId.Value,
                activeShopId.Value,
                request.Name,
                request.Barcode,
                request.Description,
                request.Uom,
                request.IsActive),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPatch("{itemId:guid}")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> UpdateItem(Guid itemId, [FromBody] UpdateItemRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var activeShopId = GetCurrentActiveShopId();
        if (activeShopId is null)
            return new List<Error> { Errors.Shop.ActiveShopNotSelected }.ToProblemResult();

        var result = await bus.InvokeAsync<ErrorOr<Success>>(
            new UpdateItemCommand(
                userId.Value,
                activeShopId.Value,
                itemId,
                request.Name,
                request.Barcode,
                request.Description,
                request.Uom),
            cancellationToken);

        if (result.IsError)
            return result.Errors.ToList().ToProblemResult();

        return NoContent();
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

public sealed record AddItemRequest(
    string Name,
    string Barcode,
    string? Description,
    string Uom,
    bool IsActive);

public sealed record UpdateItemRequest(
    string Name,
    string Barcode,
    string? Description,
    string Uom);
