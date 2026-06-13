using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Features.CreditNotes.Commands.VoidCreditNote;
using Intelibill.Application.Features.CreditNotes.DTOs;
using Intelibill.Application.Features.CreditNotes.Queries.GetCreditNoteByCode;
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
