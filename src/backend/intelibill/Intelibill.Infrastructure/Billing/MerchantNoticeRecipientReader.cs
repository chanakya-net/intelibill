using System.Data;
using ErrorOr;
using Intelibill.Application.Common.Errors;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Infrastructure.Licensing;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Intelibill.Shared.Web.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Infrastructure.Billing;

public interface INoticeRecipientAuthorityReceiver
{
    Task<ErrorOr<NoticeRecipientAuthorityResponseV1>> InspectAsync(
        NoticeRecipientAuthorityRequestV1 request,
        VerifiedServiceAssertion proof,
        CancellationToken ct);
}

/// <summary>
/// Answers licensing's signed notice-recipient query with live verified merchant identities,
/// preferred languages, and paged active shop memberships. Consumes the single-use service
/// assertion in a Read Committed transaction under shop-scoped RLS when a shop is named.
/// </summary>
internal sealed class MerchantNoticeRecipientReader(
    ApplicationDbContext db,
    EntitlementProjectionStore projection,
    TimeProvider transportClock,
    [FromKeyedServices(ShopTrialsV1.DeliverySection)] TimeProvider businessClock)
    : INoticeRecipientAuthorityReceiver
{
    public async Task<ErrorOr<NoticeRecipientAuthorityResponseV1>> InspectAsync(
        NoticeRecipientAuthorityRequestV1 request,
        VerifiedServiceAssertion proof,
        CancellationToken ct)
    {
        if (!request.IsValid())
            return Errors.Notifications.RecipientAuthorityInvalid;
        if (request.Deadline <= transportClock.GetUtcNow() || request.Deadline > proof.ExpiresAt)
            return Errors.Notifications.RecipientAuthorityUnauthorized;

        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.SetLockTimeoutAsync(LockWait.Brief, ct);
            if (request.ShopId is { } shopId)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    SELECT set_config('app.active_shop_id', {shopId.ToString("D")}, true),
                           set_config('app.entitlement_account_id', {request.AccountId.ToString("D")}, true)
                    """, ct);
            }

            if (!await projection.ConsumeAsync(proof, ct))
                return Errors.Notifications.RecipientAuthorityUnauthorized;

            var observedAt = EntitlementSnapshotsV1.Instant(businessClock);
            var affiliationMatches = true;
            NoticeRecipientActorV1[] shopMembers = [];
            Guid? nextCursor = null;

            if (request.ShopId is { } targetShop)
            {
                affiliationMatches = await MatchesKnownAffiliationAsync(targetShop, request.AccountId, ct);
                if (affiliationMatches)
                    (shopMembers, nextCursor) = await ReadShopMemberPageAsync(targetShop, request.AfterUserId, request.PageSize, ct);
            }

            var actors = request.ActorIds.Length > 0
                ? await ReadRequestedActorsAsync(request.ActorIds, request.ShopId, ct)
                : [];

            if (transportClock.GetUtcNow() >= request.Deadline)
                return Errors.Notifications.RecipientAuthorityUnauthorized;

            await tx.CommitAsync(ct);
            return new NoticeRecipientAuthorityResponseV1(
                LicenseNoticesV1.SchemaVersion,
                request.Challenge,
                request.AccountId,
                request.ShopId,
                request.AffiliationRequestId,
                affiliationMatches,
                observedAt,
                actors,
                shopMembers,
                nextCursor);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return Errors.Notifications.RecipientAuthorityUnavailable;
        }
    }

    private async Task<bool> MatchesKnownAffiliationAsync(Guid shopId, Guid expectedAccountId, CancellationToken ct)
    {
        var projectedAccounts = await db.Database.SqlQuery<Guid>($"""
            SELECT account_id AS "Value"
            FROM public.shop_license_states
            WHERE shop_id = {shopId}
            """).ToListAsync(ct);
        if (projectedAccounts is [var actualAccount])
            return actualAccount == expectedAccountId;

        return await db.Shops.AsNoTracking().AnyAsync(shop => shop.Id == shopId, ct);
    }

    private async Task<(NoticeRecipientActorV1[] Members, Guid? NextCursor)> ReadShopMemberPageAsync(
        Guid shopId,
        Guid? afterUserId,
        int pageSize,
        CancellationToken ct)
    {
        var query = from membership in db.ShopMemberships.AsNoTracking()
            join user in db.Users.AsNoTracking() on membership.UserId equals user.Id
            where membership.ShopId == shopId
                && membership.Status == MembershipStatus.Active
            select new
            {
                user.Id,
                user.IsLoginEnabled,
                user.IsEmailVerified,
                user.Email,
                user.IsPhoneVerified,
                user.PhoneNumber,
                user.Language,
                membership.Role,
                membership.AuthorityVersion,
            };

        if (afterUserId is { } cursor)
            query = query.Where(row => row.Id.CompareTo(cursor) > 0);

        var rows = await query
            .OrderBy(row => row.Id)
            .Take(pageSize + 1)
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var page = rows.Take(pageSize)
            .Select(row => ToActor(
                row.Id,
                row.IsLoginEnabled,
                row.IsEmailVerified,
                row.Email,
                row.IsPhoneVerified,
                row.PhoneNumber,
                row.Language,
                row.Role.ToString(),
                row.AuthorityVersion))
            .ToArray();
        var nextCursor = hasMore && page.Length > 0 ? page[^1].UserId : (Guid?)null;
        return (page, nextCursor);
    }

    private async Task<NoticeRecipientActorV1[]> ReadRequestedActorsAsync(
        Guid[] actorIds,
        Guid? shopId,
        CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .Where(user => actorIds.Contains(user.Id))
            .OrderBy(user => user.Id)
            .Select(user => new
            {
                user.Id,
                user.IsLoginEnabled,
                user.IsEmailVerified,
                user.Email,
                user.IsPhoneVerified,
                user.PhoneNumber,
                user.Language,
            })
            .ToListAsync(ct);

        Dictionary<Guid, (ShopRole Role, long AuthorityVersion)> roles = [];
        if (shopId is { } targetShop && users.Count > 0)
        {
            var foundIds = users.Select(user => user.Id).ToArray();
            var memberships = await db.ShopMemberships.AsNoTracking()
                .Where(m => m.ShopId == targetShop
                    && m.Status == MembershipStatus.Active
                    && foundIds.Contains(m.UserId))
                .Select(m => new { m.UserId, m.Role, m.AuthorityVersion })
                .ToListAsync(ct);
            roles = memberships.ToDictionary(m => m.UserId, m => (m.Role, m.AuthorityVersion));
        }

        return
        [
            .. users.Select(user =>
            {
                var hasRole = roles.TryGetValue(user.Id, out var role);
                return ToActor(
                    user.Id,
                    user.IsLoginEnabled,
                    user.IsEmailVerified,
                    user.Email,
                    user.IsPhoneVerified,
                    user.PhoneNumber,
                    user.Language,
                    hasRole && user.IsLoginEnabled ? role.Role.ToString() : null,
                    hasRole && user.IsLoginEnabled ? role.AuthorityVersion : null);
            }),
        ];
    }

    private static NoticeRecipientActorV1 ToActor(
        Guid userId,
        bool isLoginEnabled,
        bool isEmailVerified,
        string? email,
        bool isPhoneVerified,
        string? phone,
        string? language,
        string? shopRole,
        long? authorityVersion)
    {
        var verifiedEmail = isLoginEnabled && isEmailVerified && !string.IsNullOrWhiteSpace(email)
            ? email.Trim().ToLowerInvariant()
            : null;
        var verifiedPhone = isLoginEnabled && isPhoneVerified && !string.IsNullOrWhiteSpace(phone)
            ? phone.Trim()
            : null;
        return new NoticeRecipientActorV1(
            userId,
            isLoginEnabled,
            isLoginEnabled && isEmailVerified && verifiedEmail is not null,
            verifiedEmail,
            isLoginEnabled && isPhoneVerified && verifiedPhone is not null,
            verifiedPhone,
            LicenseNoticesV1.NormalizeLocale(language),
            shopRole,
            authorityVersion);
    }
}
