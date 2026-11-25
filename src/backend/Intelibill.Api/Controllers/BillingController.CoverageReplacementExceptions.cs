using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

public sealed partial class BillingController
{
    private const string ExceptionRoute = "{accountId:guid}/subscriptions/{termId:guid}/coverage-replacement-exceptions";
    private const string ExceptionConsentRoute = "{accountId:guid}/coverage-replacement-exceptions/{shopId:guid}/{exceptionId:guid}/consent";

    [HttpGet(ExceptionRoute)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> CoverageReplacementExceptions(Guid accountId, Guid termId,
        [FromServices] IMerchantCoverageReplacementExceptions exceptions, CancellationToken ct, [FromQuery] int page = 1)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await exceptions.CoverageExceptionsAsync(caller, accountId, termId, page, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpPost(ExceptionRoute)]
    [RequestSizeLimit(CoverageReplacementExceptionsV1.MaxRequestBytes)]
    [RateLimit(Limit = 30, PeriodInMinutes = 60, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RequestCoverageReplacementException(Guid accountId, Guid termId,
        RequestCoverageReplacementExceptionV1 command,
        [FromServices] IMerchantCoverageReplacementExceptions exceptions, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await exceptions.RequestCoverageExceptionAsync(caller, accountId, termId, command, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet(ExceptionRoute + "/operations/{key:guid}")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RecoverCoverageReplacementException(Guid accountId, Guid termId, Guid key,
        [FromServices] IMerchantCoverageReplacementExceptions exceptions, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await exceptions.RecoverCoverageExceptionAsync(caller, accountId, termId, key, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet(ExceptionConsentRoute)]
    [LicenceAction(LicenceAction.Billing)]
    public Task<IActionResult> ReadCoverageReplacementExceptionConsent(Guid accountId, Guid shopId, Guid exceptionId,
        [FromServices] IMerchantCoverageReplacementExceptions exceptions, CancellationToken ct) =>
        ExceptionConsentDecisionAsync(accountId, shopId, exceptionId, ShopAffiliationConsentAction.Read, Guid.Empty, exceptions, ct);

    [HttpPost(ExceptionConsentRoute + "/{decision}")]
    [RequestSizeLimit(1024)]
    [RateLimit(Limit = 30, PeriodInMinutes = 15, BackoffMinutes = 5, FailClosed = true)]
    [LicenceAction(LicenceAction.Billing)]
    public Task<IActionResult> ChangeCoverageReplacementExceptionConsent(Guid accountId, Guid shopId, Guid exceptionId,
        string decision, AffiliationOperation operation,
        [FromServices] IMerchantCoverageReplacementExceptions exceptions, CancellationToken ct)
    {
        var action = decision switch
        {
            "grant" => ShopAffiliationConsentAction.Grant,
            "accept" => ShopAffiliationConsentAction.Accept,
            "revoke" => ShopAffiliationConsentAction.Revoke,
            _ => ShopAffiliationConsentAction.Read,
        };
        return action == ShopAffiliationConsentAction.Read
            ? Task.FromResult<IActionResult>(NotFound())
            : ExceptionConsentDecisionAsync(accountId, shopId, exceptionId, action, operation.OperationKey, exceptions, ct);
    }

    private async Task<IActionResult> ExceptionConsentDecisionAsync(Guid account, Guid shop, Guid exceptionId,
        ShopAffiliationConsentAction action, Guid key, IMerchantCoverageReplacementExceptions exceptions, CancellationToken ct)
    {
        if (ReadCaller() is not { } caller) return SessionInvalid();
        var result = await exceptions.CoverageExceptionConsentAsync(caller, account, shop, exceptionId, action, key, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
