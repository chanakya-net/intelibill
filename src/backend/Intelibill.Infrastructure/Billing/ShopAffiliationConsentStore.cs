using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Domain.Enums;
using Intelibill.Domain.Interfaces.Repositories;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace Intelibill.Infrastructure.Billing;

internal sealed class ShopAffiliationConsentStore(ApplicationDbContext db, IMembershipAuthorityRepository membership,
    IAuthSessionRepository sessions, TimeProvider clock)
{
    internal async Task<ErrorOr<ShopAffiliationConsentV1>> RunAsync(MerchantBillingCaller caller,
        ShopAffiliationRequestV1 request, ShopAffiliationConsentAction action, Guid operation, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var owner = await LockOwnerAsync(caller, request.ShopId, ct);
        if (owner is null)
            return ShopAffiliationConsentErrors.OwnerRequired;

        var now = clock.GetUtcNow();
        now = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
        if (action != ShopAffiliationConsentAction.Read)
        {
            var changed = await RecordAsync(request, owner, action, operation, now, ct);
            if (changed.IsError)
                return changed.Errors;
        }

        var status = await StatusAsync(request, owner, now, ct);
        await transaction.CommitAsync(ct);
        return new ShopAffiliationConsentV1(request, owner.ShopName, owner.OwnerName, status);
    }

    internal async Task<bool> IsOwnerAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct) =>
        caller.Session.ActiveShopId == shop && await db.ShopMemberships.AsNoTracking().AnyAsync(m =>
            m.ShopId == shop && m.UserId == caller.Session.UserId && m.Role == ShopRole.Owner &&
            m.Status == MembershipStatus.Active, ct);

    private async Task<LiveOwner?> LockOwnerAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct)
    {
        var claim = caller.Session;
        if (claim.ActiveShopId != shop)
            return null;
        var user = await sessions.LockUserAuthorityAsync(claim.UserId, ct);
        if (user is not { IsLoginEnabled: true } || user.SecurityVersion != claim.SecurityVersion)
            return null;
        var session = await sessions.LockAsync(claim.UserId, claim.SessionId, ct);
        if (session is null || session.ScopeVersion != claim.ScopeVersion || session.ActiveShopId != shop ||
            session.SecurityVersion != claim.SecurityVersion || !await membership.LockShopAsync(shop, ct))
            return null;
        await membership.LockMembershipAsync(shop, claim.UserId, ct);
        var now = clock.GetUtcNow();
        if (!session.IsActive(now) || now.ToUnixTimeSeconds() >= caller.AccessExpiry)
            return null;

        return await (from m in db.ShopMemberships.AsNoTracking()
            join s in db.Shops.AsNoTracking() on m.ShopId equals s.Id
            join u in db.Users.AsNoTracking() on m.UserId equals u.Id
            where m.ShopId == shop && m.UserId == claim.UserId && m.Role == ShopRole.Owner
                && m.Status == MembershipStatus.Active
                && ((u.Email != null && u.IsEmailVerified) || (u.PhoneNumber != null && u.IsPhoneVerified))
            select new LiveOwner(u.Id, m.Id, m.AuthorityVersion, s.Name, u.FirstName + " " + u.LastName)).SingleOrDefaultAsync(ct);
    }

    private async Task<ErrorOr<Success>> RecordAsync(ShopAffiliationRequestV1 request, LiveOwner owner,
        ShopAffiliationConsentAction action, Guid operation, DateTimeOffset now, CancellationToken ct)
    {
        var kind = action switch
        {
            ShopAffiliationConsentAction.Grant => "Granted",
            ShopAffiliationConsentAction.Revoke => "Revoked",
            ShopAffiliationConsentAction.Accept => "Accepted",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        var events = await Events(request.ShopId)
            .Where(row => row.RequestId == request.RequestId || (row.ActorId == owner.ActorId && row.OperationKey == operation)
                || row.Kind == "Accepted").ToArrayAsync(ct);
        if (events.SingleOrDefault(row => row.ActorId == owner.ActorId && row.OperationKey == operation) is { } replay)
            return replay.RequestId == request.RequestId && replay.Kind == kind && replay.Matches(owner)
                ? Result.Success : MerchantBillingErrors.PayloadMismatch;

        var prior = events.Where(row => row.RequestId == request.RequestId).ToArray();
        if (events.Any(row => row.Kind == "Accepted") || prior.Any(row => row.Kind is "Revoked"))
            return ShopAffiliationConsentErrors.ConsentInvalid;
        var grant = prior.SingleOrDefault(row => row.Kind == "Granted");
        if ((kind != "Revoked" && now >= request.ExpiresAt) ||
            (kind == "Granted" ? grant is not null : grant is null || !grant.Matches(owner)))
            return ShopAffiliationConsentErrors.ConsentInvalid;

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.shop_affiliation_consent_events
                (id, shop_id, account_id, request_id, actor_id, membership_id, authority_version, operation_key,
                 kind, recorded_at, term_id, shop_name, owner_name, request_expires_at)
            VALUES ({Guid.NewGuid()}, {request.ShopId}, {request.AccountId}, {request.RequestId}, {owner.ActorId},
                {owner.MembershipId}, {owner.Version}, {operation}, {kind}, {now}, {request.Term?.TermId},
                {owner.ShopName}, {owner.OwnerName}, {request.ExpiresAt})
            """, ct);
        return Result.Success;
    }

    private async Task<string> StatusAsync(ShopAffiliationRequestV1 request, LiveOwner owner, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await Events(request.ShopId).Where(row => row.RequestId == request.RequestId).ToArrayAsync(ct);
        if (rows.SingleOrDefault(row => row.Kind == "Accepted") is { } accepted)
            return accepted.Outcome ?? "Accepted";
        if (rows.Any(row => row.Kind == "Revoked"))
            return "Revoked";
        if (now >= request.ExpiresAt)
            return "Expired";
        var grant = rows.SingleOrDefault(row => row.Kind == "Granted");
        return grant is null ? "AwaitingConsent" : grant.Matches(owner) ? "Granted" : "Stale";
    }

    private IQueryable<ConsentEvent> Events(Guid shop) => db.Database.SqlQuery<ConsentEvent>($"""
        SELECT e.request_id AS request_id, e.actor_id AS actor_id, e.membership_id AS membership_id,
            e.authority_version AS version, e.operation_key AS operation_key, e.kind AS kind, r.status AS outcome
        FROM public.shop_affiliation_consent_events e
        LEFT JOIN public.shop_affiliation_delivery_receipts r ON r.event_id = e.id
        WHERE e.shop_id = {shop}
        """);

    private sealed record ConsentEvent(Guid RequestId, Guid ActorId, Guid MembershipId, long Version, Guid OperationKey,
        string Kind, string? Outcome)
    {
        internal bool Matches(LiveOwner owner) =>
            ActorId == owner.ActorId && MembershipId == owner.MembershipId && Version == owner.Version;
    }

    private sealed record LiveOwner(Guid ActorId, Guid MembershipId, long Version, string ShopName, string OwnerName);
}
