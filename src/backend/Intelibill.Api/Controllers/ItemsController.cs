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
public sealed class ItemsController : AuthenticatedControllerBase
{
    private readonly IItemCatalogStreamingService _itemCatalogStreamingService;

    public ItemsController(IMessageBus bus, IItemCatalogStreamingService itemCatalogStreamingService)
        : base(bus)
    {
        _itemCatalogStreamingService = itemCatalogStreamingService;
    }

    [HttpGet("stream")]
    public async Task StreamItems(CancellationToken cancellationToken)
    {
        if (UserId is null)
        {
            Response.StatusCode = 401;
            return;
        }

        if (ActiveShopId is null)
        {
            Response.StatusCode = 400;
            return;
        }

        var validation = await _itemCatalogStreamingService.ValidateAccessAsync(
            UserId.Value,
            ActiveShopId.Value,
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

        await foreach (var item in _itemCatalogStreamingService.StreamByShopAsync(ActiveShopId.Value, cancellationToken))
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
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<ItemDto>>>(
            new GetItemsQuery(UserId!.Value, ActiveShopId!.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("details")]
    public async Task<IActionResult> GetProductDetails(
        [FromQuery] string? name,
        [FromQuery] string? barcode,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(barcode))
            return BadRequest("Either name or barcode must be provided");

        var authorizationHeader = HttpContext.Request.Headers.Authorization.ToString();

        var result = await Bus.InvokeAsync<ErrorOr<ProductDetailsDto>>(
            new GetProductDetailsByNameOrBarcodeQuery(UserId!.Value, ActiveShopId!.Value, name, barcode, authorizationHeader),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> AddItem([FromBody] AddItemRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<ItemDto>>(
            new AddItemCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                request.Name,
                request.Barcode,
                request.Description,
                request.Uom,
                request.IsActive,
                request.HsnCode,
                request.DefaultTaxRatePercent),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPatch("{itemId:guid}")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> UpdateItem(Guid itemId, [FromBody] UpdateItemRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<Success>>(
            new UpdateItemCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                itemId,
                request.Name,
                request.Barcode,
                request.Description,
                request.Uom,
                request.HsnCode,
                request.DefaultTaxRatePercent,
                request.IsActive),
            cancellationToken);

        if (result.IsError)
            return result.Errors.ToList().ToProblemResult();

        return NoContent();
    }
}

public sealed record AddItemRequest(
    string Name,
    string Barcode,
    string? Description,
    string Uom,
    bool IsActive,
    string? HsnCode,
    decimal DefaultTaxRatePercent);

public sealed record UpdateItemRequest(
    string Name,
    string Barcode,
    string? Description,
    string Uom,
    string? HsnCode,
    decimal DefaultTaxRatePercent,
    bool? IsActive);
