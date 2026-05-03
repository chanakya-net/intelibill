using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Shops.Commands.CreateShop;
using Intelibill.Application.Features.Shops.Commands.UpdateShop;
using Intelibill.Application.Features.Shops.Commands.SetDefaultShop;
using Intelibill.Application.Features.Shops.Commands.SwitchActiveShop;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Application.Features.Shops.Queries.GetShopDetails;
using Intelibill.Application.Features.Shops.Queries.GetMyShops;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/shops")]
[Authorize]
public sealed class ShopsController : AuthenticatedControllerBase
{
    public ShopsController(IMessageBus bus) : base(bus)
    {
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyShops(CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<IReadOnlyList<UserShopDto>>>(
            new GetMyShopsQuery(UserId!.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{shopId:guid}")]
    public async Task<IActionResult> GetShopDetails(Guid shopId, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<ShopDetailsDto>>(
            new GetShopDetailsQuery(UserId!.Value, shopId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    public async Task<IActionResult> CreateShop([FromBody] CreateShopRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new CreateShopCommand(
                UserId!.Value,
                request.Name,
                request.Address,
                request.City,
                request.State,
                request.Pincode,
                request.ContactPerson,
                request.MobileNumber,
                request.GstNumber),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("switch")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> SwitchActiveShop([FromBody] SwitchActiveShopRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new SwitchActiveShopCommand(UserId!.Value, request.ShopId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("default")]
    public async Task<IActionResult> SetDefaultShop([FromBody] SetDefaultShopRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<AuthResult>>(
            new SetDefaultShopCommand(UserId!.Value, request.ShopId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPut("{shopId:guid}")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> UpdateShop(Guid shopId, [FromBody] UpdateShopRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<ShopDetailsDto>>(
            new UpdateShopCommand(
                UserId!.Value,
                shopId,
                request.Name,
                request.Address,
                request.City,
                request.State,
                request.Pincode,
                request.ContactPerson,
                request.MobileNumber,
                request.GstNumber),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}

public sealed record CreateShopRequest(
    string Name,
    string Address,
    string City,
    string State,
    string Pincode,
    string? ContactPerson,
    string? MobileNumber,
    string? GstNumber);
public sealed record SwitchActiveShopRequest(Guid ShopId);
public sealed record SetDefaultShopRequest(Guid ShopId);
public sealed record UpdateShopRequest(
    string Name,
    string Address,
    string City,
    string State,
    string Pincode,
    string? ContactPerson,
    string? MobileNumber,
    string? GstNumber);
