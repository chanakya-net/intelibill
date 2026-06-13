using ErrorOr;
using Intelibill.Api.Extensions;
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
}
