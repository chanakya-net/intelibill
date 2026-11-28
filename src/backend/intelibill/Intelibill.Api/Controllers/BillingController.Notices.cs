using Intelibill.Application.Common.Licensing;
using Intelibill.Application.Features.Billing;
using Intelibill.Shared.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace Intelibill.Api.Controllers;

/// <summary>
/// Role-appropriate licence notice reads and narrow billing contact consent routes.
/// Shop notice reads require live active-shop membership and return operational notices only;
/// account notice reads require verified active Billing Owner or Manager authority.
/// </summary>
public sealed partial class BillingController
{
    [HttpGet("/api/licensing/shops/{shopId:guid}/notices")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ShopNotices(
        Guid shopId,
        [FromQuery] Guid? after,
        [FromServices] IMerchantLicenseNotices notices,
        CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();

        var result = await notices.ReadShopNoticesAsync(caller, shopId, after, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet("{accountId:guid}/notices")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> AccountNotices(
        Guid accountId,
        [FromQuery] Guid? after,
        [FromServices] IMerchantLicenseNotices notices,
        CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();

        var result = await notices.ReadAccountNoticesAsync(caller, accountId, after, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpGet("{accountId:guid}/notice-contacts")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ListNoticeContacts(
        Guid accountId,
        [FromServices] IMerchantLicenseNotices notices,
        CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();

        var result = await notices.ListContactsAsync(caller, accountId, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpPost("{accountId:guid}/notice-contacts")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ApproveNoticeContact(
        Guid accountId,
        [FromBody] ApproveBillingContactRequestV1 request,
        [FromServices] IMerchantLicenseNotices notices,
        CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();

        var result = await notices.ApproveContactAsync(caller, accountId, request, ct);
        return result.IsError
            ? Failure(result.FirstError)
            : StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [HttpPost("{accountId:guid}/notice-contacts/{contactId:guid}/revoke")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RevokeNoticeContactByOwner(
        Guid accountId,
        Guid contactId,
        [FromBody] RevokeBillingContactRequestV1 request,
        [FromServices] IMerchantLicenseNotices notices,
        CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();

        var result = await notices.RevokeContactByOwnerAsync(caller, accountId, contactId, request, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpPost("/api/billing/notice-contacts/{contactId:guid}/consent")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> ConfirmNoticeContactConsent(
        Guid contactId,
        [FromBody] ConfirmBillingContactConsentRequestV1 request,
        [FromServices] IMerchantLicenseNotices notices,
        CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();

        var result = await notices.ConfirmSelfConsentAsync(caller, contactId, request, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }

    [HttpPost("/api/billing/notice-contacts/{contactId:guid}/revoke")]
    [LicenceAction(LicenceAction.Billing)]
    public async Task<IActionResult> RevokeNoticeContactByHolder(
        Guid contactId,
        [FromBody] RevokeBillingContactRequestV1 request,
        [FromServices] IMerchantLicenseNotices notices,
        CancellationToken ct)
    {
        if (ReadCaller() is not { } caller)
            return SessionInvalid();

        var result = await notices.RevokeContactByHolderAsync(caller, contactId, request, ct);
        return result.IsError ? Failure(result.FirstError) : Ok(result.Value);
    }
}
