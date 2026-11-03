using System.Text.Json;
using ErrorOr;
using Intelibill.Api.Extensions;
using Intelibill.Application.Common.Errors;
using Intelibill.Application.Features.OfflineReconciliation.Commands.ResolveReconciliationCase;
using Intelibill.Application.Features.OfflineReconciliation.Commands.SubmitRecoveryBundle;
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

    /// <summary>
    /// Files the invoices of a support recovery bundle (exported from a retired or revoked cashier's
    /// browser) as review cases; nothing posts until a case is approved. Owners and managers only.
    /// Resending the same <c>operationId</c> replays the committed answer.
    /// </summary>
    /// <remarks>
    /// <c>bundle</c> stays raw JSON so its <c>invoices</c> array reaches the checksum exactly as sent.
    /// A body over 2 MiB is refused with 413 while it is read, before this action runs.
    /// </remarks>
    [HttpPost("recovery-submissions")]
    [Authorize(Policy = "OwnerOrManager")]
    [RequestSizeLimit(2_097_152)]
    public async Task<IActionResult> SubmitRecoveryBundle(
        [FromBody] SubmitRecoveryBundleRequest? request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuthAndShop();
        if (auth is not null) return auth;

        var bundle = request is null ? null : ReadBundle(request.Bundle);
        if (bundle is null)
            return new List<Error> { Errors.Recovery.BundleInvalid }.ToProblemResult();

        var result = await Bus.InvokeAsync<ErrorOr<RecoverySubmissionResultDto>>(
            new SubmitRecoveryBundleCommand(
                UserId!.Value,
                ActiveShopId!.Value,
                request!.OperationId ?? string.Empty,
                bundle),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    /// <summary>The bundle's members, or null when one is missing or of the wrong JSON type.</summary>
    private static RecoveryBundle? ReadBundle(JsonElement bundle)
    {
        if (bundle.ValueKind != JsonValueKind.Object)
            return null;

        if (!TryGetString(bundle, "format", out var format)
            || !bundle.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionNumber)
            || !bundle.TryGetProperty("shopId", out var shopId) || shopId.ValueKind != JsonValueKind.String || !shopId.TryGetGuid(out var shopGuid)
            || !TryGetString(bundle, "deviceId", out var deviceId)
            || !bundle.TryGetProperty("exportedAt", out var exportedAt) || exportedAt.ValueKind != JsonValueKind.String || !exportedAt.TryGetDateTimeOffset(out var exportedAtValue)
            || !TryGetString(bundle, "checksum", out var checksum)
            || !bundle.TryGetProperty("invoices", out var invoices) || invoices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        // The exact text received, never a re-serialization: the checksum was computed over it.
        return new RecoveryBundle(format, versionNumber, shopGuid, deviceId, exportedAtValue, checksum, invoices.GetRawText());
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString()!;
        return true;
    }
}

/// <param name="OperationId">A fresh id per recovery submission, reused when retrying the same one.</param>
/// <param name="Bundle">
/// The exported bundle: <c>format</c>, <c>version</c>, <c>shopId</c>, <c>deviceId</c>, <c>exportedAt</c>,
/// <c>checksum</c> and <c>invoices</c> (each element one sync request plus <c>issuedByUserId</c>).
/// </param>
public sealed record SubmitRecoveryBundleRequest(string? OperationId, JsonElement Bundle);

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
