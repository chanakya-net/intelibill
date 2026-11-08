using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Tills;
using Intelibill.Application.Features.Tills.Commands.CreateTill;
using Intelibill.Application.Features.Tills.Commands.OpenTill;
using Intelibill.Application.Features.Tills.Commands.RecordTillCashMovement;
using Intelibill.Application.Features.Tills.Commands.CloseTill;
using Intelibill.Application.Features.Tills.Commands.ReviewTillClose;
using Intelibill.Application.Features.Tills.Queries.GetTills;
using Intelibill.Application.Features.Tills.Queries.GetTillSessions;
using Intelibill.Application.Features.Tills.Queries.GetTillSession;
using Intelibill.Application.Features.Tills.Queries.GetTillOperation;
using Intelibill.Application.Features.Tills.Queries.GetTillCapabilities;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/tills")]
[Authorize]
public sealed class TillsController(IMessageBus bus) : AuthenticatedControllerBase(bus)
{
    [HttpGet("capabilities")]
    public async Task<IActionResult> Capabilities(CancellationToken ct)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<TillCapabilitiesDto>>(new GetTillCapabilitiesQuery(ActiveShopId!.Value, UserId!.Value), ct);
        return result.ToActionResult(Ok);
    }
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<IReadOnlyList<TillDto>>>(new GetTillsQuery(ActiveShopId!.Value, UserId!.Value), ct);
        return result.ToActionResult(Ok);
    }
    [HttpPost]
    public async Task<IActionResult> Create(CreateTillRequest request, CancellationToken ct)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<TillDto>>(new CreateTillCommand(ActiveShopId!.Value, UserId!.Value, request.Name, request.OperationKey), ct);
        return result.ToActionResult(Ok);
    }
    [HttpGet("sessions")]
    public async Task<IActionResult> Sessions([FromQuery] TillSessionState? state, [FromQuery] DateOnly? businessDate,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<TillPage<TillSessionDto>>>(new GetTillSessionsQuery(ActiveShopId!.Value, UserId!.Value, state, businessDate, page, pageSize), ct);
        return result.ToActionResult(Ok);
    }
    [HttpGet("sessions/{id:guid}")]
    public async Task<IActionResult> Session(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<TillSessionDetailDto>>(new GetTillSessionQuery(ActiveShopId!.Value, UserId!.Value, id, page, pageSize), ct);
        return result.ToActionResult(Ok);
    }
    [HttpPost("{id:guid}/sessions")]
    public async Task<IActionResult> Open(Guid id, OpenTillRequest request, CancellationToken ct)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<TillSessionDto>>(new OpenTillCommand(ActiveShopId!.Value, UserId!.Value, id, request.CashierId,
            request.OpeningFloat, request.OperationKey, request.OverrideReason), ct);
        return result.ToActionResult(Ok);
    }
    [HttpPost("sessions/{id:guid}/cash-movements")]
    public async Task<IActionResult> Move(Guid id, TillCashMovementRequest request, CancellationToken ct)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<TillSessionDto>>(new RecordTillCashMovementCommand(ActiveShopId!.Value, UserId!.Value, id,
            request.Direction, request.Amount, request.Reason, request.ExpectedVersion, request.OperationKey, request.OverrideReason), ct);
        return result.ToActionResult(Ok);
    }
    [HttpPost("sessions/{id:guid}/close")]
    public async Task<IActionResult> Close(Guid id, CloseTillRequest request, CancellationToken ct)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<TillSessionDto>>(new CloseTillCommand(ActiveShopId!.Value, UserId!.Value, id,
            request.CountedCash, request.Reason, request.ExpectedVersion, request.OperationKey, request.OverrideReason, request.FinalizeOverdue), ct);
        return result.ToActionResult(Ok);
    }
    [HttpPost("sessions/{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, ReviewTillCloseRequest request, CancellationToken ct)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<TillSessionDto>>(new ReviewTillCloseCommand(ActiveShopId!.Value, UserId!.Value, id,
            request.Reason, request.ExpectedVersion, request.OperationKey), ct);
        return result.ToActionResult(Ok);
    }
    [HttpGet("operations/{type}/{key}")]
    public async Task<IActionResult> Operation(string type, string key, CancellationToken ct)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr<TillOperationDto>>(new GetTillOperationQuery(ActiveShopId!.Value, UserId!.Value, type, key), ct);
        return result.ToActionResult(Ok);
    }
}

public sealed record CreateTillRequest(string Name, string OperationKey);
public sealed record OpenTillRequest(Guid CashierId, decimal OpeningFloat, string OperationKey, string? OverrideReason = null);
/// <param name="Direction">1 for cash in, -1 for cash out.</param>
public sealed record TillCashMovementRequest(decimal Direction, decimal Amount, string Reason, uint ExpectedVersion, string OperationKey, string? OverrideReason = null);
public sealed record CloseTillRequest(decimal CountedCash, string? Reason, uint ExpectedVersion, string OperationKey, string? OverrideReason = null, bool FinalizeOverdue = false);
public sealed record ReviewTillCloseRequest(string Reason, uint ExpectedVersion, string OperationKey);
