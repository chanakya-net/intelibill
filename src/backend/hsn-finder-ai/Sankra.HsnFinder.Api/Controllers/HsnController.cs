using ErrorOr;
using HsnFinder.Api.Models.Requests;
using HsnFinder.Api.Models.Responses;
using HsnFinder.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HsnFinder.Api.Controllers;

/// <summary>
/// Provides HSN lookup endpoints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class HsnController(IHsnLookupService hsnLookupService, ILogger<HsnController> logger) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("api-key-policy")]
    [ProducesResponseType(typeof(ApiResponse<HsnLookupResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> LookupHsn([FromBody] HsnLookupRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("HSN lookup requested for product: {ProductName}", request?.ProductName);

        if (request is null || !ModelState.IsValid)
        {
            return BadRequest(ApiResponse<object>.Fail("Product name is required."));
        }

        var result = await hsnLookupService.LookupAsync(request.ProductName, cancellationToken);

        if (!result.IsError)
        {
            return Ok(ApiResponse<HsnLookupResponse>.Ok(result.Value));
        }

        var error = result.FirstError;

        return error.Type == ErrorType.Validation
            ? BadRequest(ApiResponse<object>.Fail(error.Description))
            : StatusCode(StatusCodes.Status503ServiceUnavailable, ApiResponse<object>.Fail(error.Description));
    }
}
