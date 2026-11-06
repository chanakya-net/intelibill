using System.Globalization;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.PostingPeriods.Commands.ClosePostingPeriod;
using Intelibill.Application.Features.PostingPeriods.Commands.ReopenPostingPeriod;
using Intelibill.Application.Features.PostingPeriods.DTOs;
using Intelibill.Application.Features.PostingPeriods.Queries.GetPostingPeriods;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

/// <summary>
/// Monthly posting periods: the twelve months of a fiscal year with their state and audit trail,
/// and the owner's close and reopen of one month. Who may do what is decided by the handlers
/// against the live membership, never by the token's role claim.
/// </summary>
[ApiController]
[Route("api/posting-periods")]
[Authorize]
public sealed class PostingPeriodsController(IMessageBus bus) : AuthenticatedControllerBase(bus)
{
    /// <param name="fiscalYear">The year the fiscal year starts in (2026 is April 2026 to March 2027); the current one when omitted.</param>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int? fiscalYear, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<PostingPeriodListDto>>(
            new GetPostingPeriodsQuery(ActiveShopId!.Value, UserId!.Value, fiscalYear ?? 0),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    /// <param name="period">The month, written <c>yyyy-MM</c>.</param>
    [HttpPost("{period}/close")]
    public async Task<IActionResult> Close(
        string period,
        [FromBody] PostingPeriodTransitionRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;
        if (!TryParseMonth(period, out var start))
            return new List<Error> { Errors.PostingPeriod.PeriodInvalid }.ToProblemResult();

        var result = await Bus.InvokeAsync<ErrorOr<PostingPeriodDto>>(
            new ClosePostingPeriodCommand(
                ActiveShopId!.Value,
                UserId!.Value,
                start,
                request.Reason ?? string.Empty,
                request.OperationKey ?? string.Empty),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    /// <param name="period">The month, written <c>yyyy-MM</c>.</param>
    [HttpPost("{period}/reopen")]
    public async Task<IActionResult> Reopen(
        string period,
        [FromBody] PostingPeriodTransitionRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;
        if (!TryParseMonth(period, out var start))
            return new List<Error> { Errors.PostingPeriod.PeriodInvalid }.ToProblemResult();

        var result = await Bus.InvokeAsync<ErrorOr<PostingPeriodDto>>(
            new ReopenPostingPeriodCommand(
                ActiveShopId!.Value,
                UserId!.Value,
                start,
                request.Reason ?? string.Empty,
                request.OperationKey ?? string.Empty),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    private static bool TryParseMonth(string value, out DateOnly start)
    {
        start = default;
        if (value.Length != 7 || value[4] != '-')
            return false;

        return DateOnly.TryParseExact($"{value}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start);
    }
}

/// <param name="Reason">Why the month is closed or reopened; up to 500 characters.</param>
/// <param name="OperationKey">Required: a retry with the same key replays the committed outcome.</param>
public sealed record PostingPeriodTransitionRequest(string? Reason, string? OperationKey);
