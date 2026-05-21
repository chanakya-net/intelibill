using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/ping")]
public sealed class ConnectivityController : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Ping() => Ok(new PingResponse(DateTimeOffset.UtcNow));
}

public sealed record PingResponse(DateTimeOffset ServerTime);
