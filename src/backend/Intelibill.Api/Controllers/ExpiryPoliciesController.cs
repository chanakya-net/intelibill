using Intelibill.Api.Extensions;
using Intelibill.Application.Features.Expiry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/expiry-policies")]
[Authorize]
public sealed class ExpiryPoliciesController(IMessageBus bus, ExpiryPolicyService policies) : AuthenticatedControllerBase(bus)
{
    [HttpGet]
    public Task<IActionResult> Get(CancellationToken ct) => Read(null, ct);
    [HttpGet("items/{id:guid}")]
    public Task<IActionResult> GetItem(Guid id, CancellationToken ct) => Read(id, ct);
    private async Task<IActionResult> Read(Guid? id, CancellationToken ct)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;
        return (await policies.ReadAsync(ActiveShopId!.Value, UserId!.Value, id, ct)).ToActionResult(Ok);
    }
    [HttpPut]
    public Task<IActionResult> Put([FromBody] ExpiryPolicyUpdateRequest request, CancellationToken ct) => Update(null, request, ct);
    [HttpPut("items/{id:guid}")]
    public Task<IActionResult> PutItem(Guid id, [FromBody] ExpiryPolicyUpdateRequest request, CancellationToken ct) => Update(id, request, ct);
    private async Task<IActionResult> Update(Guid? id, ExpiryPolicyUpdateRequest request, CancellationToken ct)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;
        return (await policies.UpdateAsync(ActiveShopId!.Value, UserId!.Value, id, request, ct)).ToActionResult(Ok);
    }
    [HttpGet("readiness")]
    public async Task<IActionResult> Readiness([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;
        return (await policies.ReadinessAsync(ActiveShopId!.Value, UserId!.Value, page, pageSize, ct)).ToActionResult(Ok);
    }
}
