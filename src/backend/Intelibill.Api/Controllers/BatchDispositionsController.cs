using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Expiry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
namespace Intelibill.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/inventory/batches/{id:guid}/dispositions")]
public sealed class BatchDispositionsController(IMessageBus bus, BatchDispositionService dispositions) : AuthenticatedControllerBase(bus)
{
    [HttpPost]
    public async Task<IActionResult> Post(Guid id, [FromBody] BatchDispositionRequest request, CancellationToken ct)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        return (await dispositions.PostAsync(ActiveShopId!.Value, UserId!.Value, id, request, ct)).ToActionResult(Ok);
    }
    [HttpGet]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var auth = CheckAuthAndShop(); if (auth is not null) return auth;
        return (await dispositions.HistoryAsync(ActiveShopId!.Value, UserId!.Value, id, ct)).ToActionResult(Ok);
    }
}
