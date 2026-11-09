using System.Text.Json.Serialization;
using Intelibill.Api.Extensions;
using Intelibill.Api.Middleware.RateLimiting;
using Intelibill.Application.Features.Auth.DTOs;
using Intelibill.Application.Features.Shops.Commands.CreateShop;
using Intelibill.Application.Features.Shops.Commands.DeclareShopTaxProfile;
using Intelibill.Application.Features.Shops.Commands.SetDefaultShop;
using Intelibill.Application.Features.Shops.Commands.SwitchActiveShop;
using Intelibill.Application.Features.Shops.Commands.UpdateShop;
using Intelibill.Application.Features.Shops.Commands.UpdateShopBankDetails;
using Intelibill.Application.Features.Shops.DTOs;
using Intelibill.Application.Features.Shops.TillSettings;
using Intelibill.Application.Features.Shops.Queries.GetShopDetails;
using Intelibill.Application.Features.Shops.Queries.GetMyShops;
using Intelibill.Application.Features.Users.Commands.InviteShopMember;
using Intelibill.Application.Features.Users.Commands.RevokeShopInvitation;
using Intelibill.Application.Features.Users.Commands.UpdateShopMembership;
using Intelibill.Application.Features.Users.Queries.GetShopInvitations;
using Intelibill.Application.Features.Users.DTOs;
using Intelibill.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Wolverine;

namespace Intelibill.Api.Controllers;

[ApiController]
[Route("api/shops")]
[Authorize]
public sealed class ShopsController : AuthenticatedControllerBase
{
    public ShopsController(IMessageBus bus) : base(bus)
    {
    }

    [HttpGet("{shopId:guid}/till-settings")]
    public async Task<IActionResult> GetTillSettings(Guid shopId, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<ShopTillSettingsDto>>(
            new GetShopTillSettingsQuery(shopId, UserId!.Value, ActiveShopId), cancellationToken);
        return result.ToActionResult(Ok);
    }

    [HttpPut("{shopId:guid}/till-settings")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> UpdateTillSettings(Guid shopId, [FromBody] UpdateShopTillSettingsRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;
        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<ShopTillSettingsDto>>(
            new UpdateShopTillSettingsCommand(shopId, UserId!.Value, ActiveShopId, request.Enabled,
                request.ExpectedVersion, request.Reason, request.Confirmed), cancellationToken);
        return result.ToActionResult(Ok);
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMyShops(CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<IReadOnlyList<UserShopDto>>>(
            new GetMyShopsQuery(UserId!.Value),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpGet("{shopId:guid}")]
    public async Task<IActionResult> GetShopDetails(Guid shopId, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<ShopDetailsDto>>(
            new GetShopDetailsQuery(UserId!.Value, shopId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost]
    public async Task<IActionResult> CreateShop([FromBody] CreateShopRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var session = CheckSession();
        if (session is not null) return session;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<AccessScopeResult>>(
            new CreateShopCommand(
                UserId!.Value,
                SessionId!.Value,
                request.Name,
                request.Address,
                request.City,
                request.State,
                request.Pincode,
                request.ContactPerson,
                request.MobileNumber,
                request.GstNumber,
                request.TaxProfile?.ToInput()),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    /// <summary>
    /// Declares the GST registration the shop issues documents under. Sales and offline ranges are
    /// refused until a profile is declared.
    /// </summary>
    [HttpPut("{shopId:guid}/tax-profile")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> DeclareTaxProfile(Guid shopId, [FromBody] DeclareShopTaxProfileRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<ShopDetailsDto>>(
            new DeclareShopTaxProfileCommand(UserId!.Value, shopId, request.ToInput(), request.GstNumber),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    /// <summary>
    /// Selects one of the caller's assigned shops. Membership, not ownership, controls selection:
    /// the handler checks the caller holds a live membership in the requested shop.
    /// </summary>
    [HttpPost("switch")]
    public async Task<IActionResult> SwitchActiveShop([FromBody] SwitchActiveShopRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var session = CheckSession();
        if (session is not null) return session;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<AccessScopeResult>>(
            new SwitchActiveShopCommand(UserId!.Value, SessionId!.Value, request.ShopId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPost("default")]
    public async Task<IActionResult> SetDefaultShop([FromBody] SetDefaultShopRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<ShopPreferenceResult>>(
            new SetDefaultShopCommand(UserId!.Value, request.ShopId, ActiveShopId, SecurityVersion),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    [HttpPut("{shopId:guid}")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> UpdateShop(Guid shopId, [FromBody] UpdateShopRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<ShopDetailsDto>>(
            new UpdateShopCommand(
                UserId!.Value,
                shopId,
                request.Name,
                request.Address,
                request.City,
                request.State,
                request.Pincode,
                request.ContactPerson,
                request.MobileNumber,
                request.GstNumber),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    /// <summary>
    /// Changes one member's shop-scoped role and status. Ownership of the shop is re-read live
    /// inside the handler's authority lock, so the caller's <c>active_shop_role</c> claim is not
    /// what authorizes the change and the shop need not be the caller's active one.
    /// </summary>
    [HttpPatch("{shopId:guid}/members/{targetUserId:guid}")]
    public async Task<IActionResult> UpdateShopMembership(
        Guid shopId,
        Guid targetUserId,
        [FromBody] UpdateShopMembershipRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<MembershipDto>>(
            new UpdateShopMembershipCommand(
                UserId!.Value,
                shopId,
                targetUserId,
                request.Role,
                request.Status,
                request.ExpectedVersion),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    /// <summary>
    /// Invites one email address to this shop. The invitation grants nothing until the person who
    /// owns that verified address accepts it from their own account.
    /// </summary>
    // Sends mail to an address the caller chooses, so it shares the destination budget with every
    // other message an address can be sent.
    [HttpPost("{shopId:guid}/invitations")]
    [RateLimit(Limit = 20, PeriodInMinutes = 60, BackoffMinutes = 15, Subject = RateLimitSubject.Destination, FailClosed = true)]
    public async Task<IActionResult> InviteShopMember(
        Guid shopId,
        [FromBody] InviteShopMemberRequest request,
        CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<ShopInvitationDto>>(
            new InviteShopMemberCommand(UserId!.Value, shopId, request.Email, request.Role),
            cancellationToken);

        return result.ToActionResult(invitation => StatusCode(StatusCodes.Status201Created, invitation));
    }

    [HttpGet("{shopId:guid}/invitations")]
    public async Task<IActionResult> GetShopInvitations(Guid shopId, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<IReadOnlyList<ShopInvitationDto>>>(
            new GetShopInvitationsQuery(UserId!.Value, shopId),
            cancellationToken);

        return result.ToActionResult(Ok);
    }

    /// <summary>Withdraws a still-pending invitation, making its link unusable.</summary>
    [HttpDelete("{shopId:guid}/invitations/{invitationId:guid}")]
    public async Task<IActionResult> RevokeShopInvitation(Guid shopId, Guid invitationId, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<bool>>(
            new RevokeShopInvitationCommand(UserId!.Value, shopId, invitationId),
            cancellationToken);

        return result.ToActionResult(_ => NoContent());
    }

    [HttpPut("{shopId:guid}/bank-details")]
    public async Task<IActionResult> UpdateShopBankDetails(Guid shopId, [FromBody] UpdateShopBankDetailsRequest request, CancellationToken cancellationToken)
    {
        var auth = CheckAuth();
        if (auth is not null) return auth;

        var result = await Bus.InvokeAsync<ErrorOr.ErrorOr<ShopDetailsDto>>(
            new UpdateShopBankDetailsCommand(
                UserId!.Value,
                shopId,
                request.BankName,
                request.AccountNumber,
                request.AccountType,
                request.IfscCode,
                request.AccountHolderName),
            cancellationToken);

        return result.ToActionResult(Ok);
    }
}

public sealed record CreateShopRequest(
    string Name,
    string Address,
    string City,
    string State,
    string Pincode,
    string? ContactPerson,
    string? MobileNumber,
    string? GstNumber,
    ShopTaxProfileRequest? TaxProfile = null);
public sealed record ShopTaxProfileRequest(
    string? LegalName,
    string? StateCode,
    TaxRegime TaxRegime,
    string? InvoiceSeriesCode,
    EInvoiceStatus? EInvoiceStatus)
{
    public ShopTaxProfileInput ToInput() => new(LegalName, StateCode, TaxRegime, InvoiceSeriesCode, EInvoiceStatus);
}
public sealed record DeclareShopTaxProfileRequest(
    string? LegalName,
    string? StateCode,
    TaxRegime TaxRegime,
    string? GstNumber,
    string? InvoiceSeriesCode,
    EInvoiceStatus? EInvoiceStatus)
{
    public ShopTaxProfileInput ToInput() => new(LegalName, StateCode, TaxRegime, InvoiceSeriesCode, EInvoiceStatus);
}
public sealed record SwitchActiveShopRequest(Guid ShopId);
public sealed record UpdateShopMembershipRequest(string Role, string Status, long ExpectedVersion);
public sealed record InviteShopMemberRequest(string Email, string Role) : IRateLimitSubject
{
    string? IRateLimitSubject.Subject => Email;
}
public sealed record SetDefaultShopRequest(Guid ShopId);
public sealed record UpdateShopRequest(
    string Name,
    string Address,
    string City,
    string State,
    string Pincode,
    string? ContactPerson,
    string? MobileNumber,
    string? GstNumber);
public sealed record UpdateShopBankDetailsRequest(
    string? BankName,
    string? AccountNumber,
    string? AccountType,
    string? IfscCode,
    string? AccountHolderName);

public sealed record UpdateShopTillSettingsRequest([property: JsonRequired] bool Enabled, [property: JsonRequired] int ExpectedVersion,
    string Reason, [property: JsonRequired] bool Confirmed);
