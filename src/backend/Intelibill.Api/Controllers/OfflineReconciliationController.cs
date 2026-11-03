using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase;
using Intelibill.Application.Features.OfflineReconciliation.DTOs;
using Intelibill.Application.Features.OfflineReconciliation.Queries.GetReconciliationCase;
using Intelibill.Application.Features.OfflineReconciliation.Queries.GetReconciliationCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

/// <summary>
/// The offline reconciliation console: review cases filed while syncing offline invoices, and take
/// the allowlisted, idempotent, version-checked actions that resolve them.
/// </summary>
[ApiController]
[Route("api/offline-reconciliation")]
[Authorize(Policy = "OwnerManagerOrStaff")]
public sealed class OfflineReconciliationController : AuthenticatedControllerBase
{
    public OfflineReconciliationController(IMessageBus bus) : base(bus)
    {
    }

    /// <summary>One page of cases, newest first. Staff see only the cases they filed.</summary>
    [HttpGet]
    public async Task<IActionResult> GetCases(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<ReconciliationCasePageDto>>(
            new GetReconciliationCasesQuery(UserId!.Value, ActiveShopId!.Value, status, page, pageSize),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{caseId:guid}")]
    public async Task<IActionResult> GetCase(Guid caseId, CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr<ReconciliationCaseDto>>(
            new GetReconciliationCaseQuery(UserId!.Value, ActiveShopId!.Value, caseId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    /// <summary>
    /// Takes one action on the case and answers the case as committed. Resending the same
    /// <c>operationId</c> replays that answer; a stale <c>expectedVersion</c> answers 409.
    /// </summary>
    [HttpPost("{caseId:guid}/resolve")]
    public async Task<IActionResult> ResolveCase(
        Guid caseId,
        [FromBody] ResolveReconciliationCaseRequest? request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        if (request is null)
            return new List<Error> { Errors.Reconciliation.UnknownAction }.ToProblemResult();

        var result = await Bus.InvokeAsync<ErrorOr<ReconciliationCaseDto>>(
            new ResolveReconciliationCaseCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                caseId,
                request.OperationId ?? string.Empty,
                request.ExpectedVersion,
                request.Action ?? string.Empty,
                request.Reason ?? string.Empty,
                request.LinkedOperationId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}

/// <param name="OperationId">A fresh id per resolution intent, reused when retrying the same one.</param>
/// <param name="ExpectedVersion">The case <c>version</c> the reviewer was shown.</param>
/// <param name="Action">One of the case's <c>availableActions</c>.</param>
/// <param name="Reason">Required, up to 500 characters.</param>
/// <param name="LinkedOperationId">The adjustment (<c>LinkStockCorrection</c>) or sale (<c>LinkExistingSale</c>).</param>
public sealed record ResolveReconciliationCaseRequest(
    string? OperationId,
    uint ExpectedVersion,
    string? Action,
    string? Reason,
    Guid? LinkedOperationId);
