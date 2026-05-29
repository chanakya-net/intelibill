using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.Services.Commands.ActivateService;
using Intelibill.Application.Features.Services.Commands.AddService;
using Intelibill.Application.Features.Services.Commands.DeactivateService;
using Intelibill.Application.Features.Services.Commands.UpdateService;
using Intelibill.Application.Features.Services.DTOs;
using Intelibill.Application.Features.Services.Queries.GetServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/services")]
[Authorize]
public sealed class ServicesController : AuthenticatedControllerBase
{
    public ServicesController(IMessageBus bus) : base(bus)
    {
    }

    [HttpGet]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> GetServices(
        [FromQuery] bool includeInactive = true,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<ServiceDto>>>(
            new GetServicesQuery(UserId!.Value, ActiveShopId!.Value, includeInactive, search),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> AddService([FromBody] AddServiceRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<ServiceDto>>(
            new AddServiceCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                request.Name,
                request.Description,
                request.Price,
                request.HsnCode,
                request.TaxRatePercent,
                request.TaxIncluded,
                request.IsActive),
            cancellationToken);

        return result.ToActionResult(service => CreatedAtAction(nameof(GetServices), service));
    }

    [HttpPatch("{serviceId:guid}")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> UpdateService(Guid serviceId, [FromBody] UpdateServiceRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<Success>>(
            new UpdateServiceCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                serviceId,
                request.Name,
                request.Description,
                request.Price,
                request.HsnCode,
                request.TaxRatePercent,
                request.TaxIncluded),
            cancellationToken);

        if (result.IsError)
            return result.Errors.ToList().ToProblemResult();

        return NoContent();
    }

    [HttpPost("{serviceId:guid}/activate")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> ActivateService(Guid serviceId, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<Success>>(
            new ActivateServiceCommand(UserId!.Value, ActiveShopId!.Value, serviceId),
            cancellationToken);

        if (result.IsError)
            return result.Errors.ToList().ToProblemResult();

        return NoContent();
    }

    [HttpPost("{serviceId:guid}/deactivate")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> DeactivateService(Guid serviceId, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<Success>>(
            new DeactivateServiceCommand(UserId!.Value, ActiveShopId!.Value, serviceId),
            cancellationToken);

        if (result.IsError)
            return result.Errors.ToList().ToProblemResult();

        return NoContent();
    }
}

public sealed record AddServiceRequest(
    string Name,
    string? Description,
    decimal Price,
    string? HsnCode,
    decimal TaxRatePercent,
    bool TaxIncluded,
    bool IsActive);

public sealed record UpdateServiceRequest(
    string Name,
    string? Description,
    decimal Price,
    string? HsnCode,
    decimal TaxRatePercent,
    bool TaxIncluded);
