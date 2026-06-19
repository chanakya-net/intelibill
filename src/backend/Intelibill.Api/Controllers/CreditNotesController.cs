using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.CreditNotes.Commands.VoidCreditNote;
using Intelibill.Application.Features.CreditNotes.DTOs;
using Intelibill.Application.Features.CreditNotes.Queries.GetCreditNoteByCode;
using Intelibill.Application.Features.CreditNotes.Queries.GetCreditNotePrintByCode;
using Intelibill.Application.Features.CreditNotes.Queries.GetCreditNotes;
using Intelibill.Application.Features.Expenses.DTOs;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/credit-notes")]
[Authorize]
public sealed class CreditNotesController : AuthenticatedControllerBase
{
    public CreditNotesController(IMessageBus bus) : base(bus)
    {
    }

    [HttpGet]
    [Authorize(Policy = "OwnerManagerOrStaff")]
    public async Task<IActionResult> GetCreditNotes(
        [FromQuery] string? search,
        [FromQuery] CreditNoteStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PaginatedList<CreditNoteListItemDto>>>(
            new GetCreditNotesQuery(UserId!.Value, ActiveShopId!.Value, search, status, page, pageSize),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{code}")]
    [Authorize(Policy = "OwnerManagerOrStaff")]
    public async Task<IActionResult> GetCreditNoteByCode(string code, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<CreditNoteDto>>(
            new GetCreditNoteByCodeQuery(UserId!.Value, ActiveShopId!.Value, code),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{code}/print")]
    [Authorize(Policy = "OwnerManagerOrStaff")]
    public async Task<IActionResult> GetCreditNotePrintByCode(string code, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<CreditNotePrintDto>>(
            new GetCreditNotePrintByCodeQuery(UserId!.Value, ActiveShopId!.Value, code),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("{code}/void")]
    [Authorize(Policy = "OwnerOrManager")]
    public async Task<IActionResult> VoidCreditNote(
        string code,
        [FromBody] VoidCreditNoteRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<Success>>(
            new VoidCreditNoteCommand(UserId!.Value, ActiveShopId!.Value, code, request.Reason),
            cancellationToken);

        return result.ToActionResult(_ => NoContent());
    }
}

public sealed record VoidCreditNoteRequest(string Reason);
