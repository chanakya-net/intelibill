using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Api.AdminIdentities;
using Intelibill.Licensing.Api.Billing;
using Intelibill.Licensing.Api.Catalogue;
using Intelibill.Licensing.Api.Status;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Billing;
using Intelibill.Licensing.Application.Notifications;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.AspNetCore.Authorization;

namespace Intelibill.Licensing.Api.Notifications;

internal static class LicenseNoticeEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static void MapCustomerLicenseNotices(this WebApplication app, AuthorizationPolicy policy)
    {
        const string shop = MerchantBillingDelegationV1.InternalPrefix + "/shops/{shop:guid}/notices";
        app.MapGet(shop, (HttpContext context, Guid shop, ReadLicenseNoticesHandler handler, CancellationToken ct) =>
            ReadShopAsync(context, shop, null, handler, ct)).RequireAuthorization(policy);
        app.MapGet(shop + "/after/{after:guid}", (HttpContext context, Guid shop, Guid after,
            ReadLicenseNoticesHandler handler, CancellationToken ct) =>
            ReadShopAsync(context, shop, after, handler, ct)).RequireAuthorization(policy);

        const string account = MerchantBillingDelegationV1.AccountsPath + "/{account:guid}/notices";
        app.MapGet(account, (HttpContext context, Guid account, ReadLicenseNoticesHandler handler, CancellationToken ct) =>
            ReadAccountAsync(context, account, null, handler, ct)).RequireAuthorization(policy);
        app.MapGet(account + "/after/{after:guid}", (HttpContext context, Guid account, Guid after,
            ReadLicenseNoticesHandler handler, CancellationToken ct) =>
            ReadAccountAsync(context, account, after, handler, ct)).RequireAuthorization(policy);

        const string contacts = MerchantBillingDelegationV1.AccountsPath + "/{account:guid}/notice-contacts";
        app.MapGet(contacts, (HttpContext context, Guid account, BillingContactConsentHandler handler, CancellationToken ct) =>
            ListContactsAsync(context, account, null, handler, ct)).RequireAuthorization(policy);
        app.MapGet(contacts + "/after/{after:guid}", (HttpContext context, Guid account, Guid after,
            BillingContactConsentHandler handler, CancellationToken ct) =>
            ListContactsAsync(context, account, after, handler, ct)).RequireAuthorization(policy);
        app.MapPost(contacts, ApproveContactAsync).RequireAuthorization(policy);
        app.MapPost(contacts + "/{contactId:guid}/revoke", RevokeContactByOwnerAsync).RequireAuthorization(policy);

        const string self = MerchantBillingDelegationV1.InternalPrefix + "/notice-contacts/{contactId:guid}";
        app.MapPost(self + "/consent", ConfirmSelfConsentAsync).RequireAuthorization(policy);
        app.MapPost(self + "/revoke", RevokeContactByHolderAsync).RequireAuthorization(policy);
    }

    internal static void MapAdminLicenseNotices(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/accounts/{account:guid}")
            .RequireAuthorization(ProviderSessions.StatusPolicy);
        group.MapGet("/notifications", ReadAdminPreviewAsync);
    }

    private static async Task<IResult> ReadAdminPreviewAsync(
        HttpContext context,
        Guid account,
        Guid? after,
        ReadLicenseNoticesHandler handler,
        CancellationToken ct)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (ProviderSessions.ReadCaller(context.User) is not { } caller)
            return CatalogueDraftEndpoints.Problem(context, AdminRefreshErrors.SessionEnded);

        var result = await handler.ReadProviderPreviewAsync(caller, account, after, ct);
        return result.IsError
            ? CatalogueDraftEndpoints.Problem(context, result.FirstError)
            : Results.Json(result.Value);
    }

    private static async Task<IResult> ReadShopAsync(
        HttpContext context,
        Guid shop,
        Guid? after,
        ReadLicenseNoticesHandler handler,
        CancellationToken ct)
    {
        var proof = CustomerSubscriptionStatusEndpoints.ReadProof(
            context,
            null,
            LicenseNoticesV1.ShopRead,
            LicenseNoticesV1.ShopNoticesPath(shop, after));
        return proof is null
            ? Invalid(context)
            : Respond(context, await handler.ReadShopAsync(proof, shop, after, ct));
    }

    private static async Task<IResult> ReadAccountAsync(
        HttpContext context,
        Guid account,
        Guid? after,
        ReadLicenseNoticesHandler handler,
        CancellationToken ct)
    {
        var proof = CustomerSubscriptionStatusEndpoints.ReadProof(
            context,
            account,
            LicenseNoticesV1.AccountRead,
            LicenseNoticesV1.AccountNoticesPath(account, after));
        return proof is null
            ? Invalid(context)
            : Respond(context, await handler.ReadAccountAsync(proof, account, after, ct));
    }

    private static async Task<IResult> ListContactsAsync(
        HttpContext context,
        Guid account,
        Guid? after,
        BillingContactConsentHandler handler,
        CancellationToken ct)
    {
        var proof = CustomerSubscriptionStatusEndpoints.ReadProof(
            context,
            account,
            LicenseNoticesV1.ContactList,
            LicenseNoticesV1.AccountContactsPath(account, after));
        return proof is null
            ? Invalid(context)
            : Respond(context, await handler.ListAsync(proof, account, after, ct));
    }

    private static async Task<IResult> ApproveContactAsync(
        HttpContext context,
        Guid account,
        BillingContactConsentHandler handler,
        CancellationToken ct)
    {
        var body = await ReadVerifiedPostAsync<ApproveBillingContactRequestV1>(
            context,
            account,
            LicenseNoticesV1.ContactApprove,
            LicenseNoticesV1.AccountContactsPath(account),
            ct);
        if (body is not { } verified)
            return Invalid(context);

        var result = await handler.ApproveAsync(verified.Proof, account, verified.Payload, ct);
        return result.IsError
            ? CustomerBillingEndpoints.Failure(context, result.FirstError)
            : Results.Json(result.Value, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> RevokeContactByOwnerAsync(
        HttpContext context,
        Guid account,
        Guid contactId,
        BillingContactConsentHandler handler,
        CancellationToken ct)
    {
        var body = await ReadVerifiedPostAsync<RevokeBillingContactRequestV1>(
            context,
            account,
            LicenseNoticesV1.ContactOwnerRevoke,
            LicenseNoticesV1.ContactOwnerRevokePath(account, contactId),
            ct);
        if (body is not { } verified)
            return Invalid(context);

        return Respond(context, await handler.RevokeByOwnerAsync(verified.Proof, account, contactId, verified.Payload, ct));
    }

    private static async Task<IResult> ConfirmSelfConsentAsync(
        HttpContext context,
        Guid contactId,
        BillingContactConsentHandler handler,
        CancellationToken ct)
    {
        var body = await ReadVerifiedPostAsync<ConfirmBillingContactConsentRequestV1>(
            context,
            null,
            LicenseNoticesV1.ContactSelfConsent,
            LicenseNoticesV1.ContactSelfConsentPath(contactId),
            ct);
        if (body is not { } verified)
            return Invalid(context);

        return Respond(context, await handler.ConfirmSelfConsentAsync(verified.Proof, contactId, verified.Payload, ct));
    }

    private static async Task<IResult> RevokeContactByHolderAsync(
        HttpContext context,
        Guid contactId,
        BillingContactConsentHandler handler,
        CancellationToken ct)
    {
        var body = await ReadVerifiedPostAsync<RevokeBillingContactRequestV1>(
            context,
            null,
            LicenseNoticesV1.ContactSelfRevoke,
            LicenseNoticesV1.ContactSelfRevokePath(contactId),
            ct);
        if (body is not { } verified)
            return Invalid(context);

        return Respond(context, await handler.RevokeByHolderAsync(verified.Proof, contactId, verified.Payload, ct));
    }

    private static async Task<(VerifiedBillingDelegation Proof, T Payload)?> ReadVerifiedPostAsync<T>(
        HttpContext context,
        Guid? account,
        string action,
        string path,
        CancellationToken ct) where T : class
    {
        var proof = context.Features.Get<VerifiedBillingDelegation>();
        if (proof is null
            || proof.AccountId != account
            || !CustomerBillingEndpoints.MatchesBinding(context.Request, proof, action, HttpMethods.Post, path))
        {
            return null;
        }

        var buffer = new byte[4096 + 1];
        var read = await context.Request.Body.ReadAtLeastAsync(buffer, buffer.Length, false, ct);
        if (read == 0 || read > 4096)
            return null;

        var bytes = buffer.AsSpan(0, read).ToArray();
        if (!string.Equals(SignedServiceAssertion.Digest(bytes), proof.BodySha256, StringComparison.Ordinal))
            return null;

        try
        {
            var payload = JsonSerializer.Deserialize<T>(bytes, Json);
            return payload is null ? null : (proof, payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult Invalid(HttpContext context) =>
        CustomerBillingEndpoints.Problem(context, StatusCodes.Status401Unauthorized, BillingDelegationErrors.Invalid);

    private static IResult Respond<T>(HttpContext context, ErrorOr<T> result) =>
        result.IsError ? CustomerBillingEndpoints.Failure(context, result.FirstError) : Results.Json(result.Value);
}
