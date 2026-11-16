using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Features.Billing;
using Intelibill.Domain.Enums;
using Intelibill.Infrastructure.Data;
using Intelibill.Shared.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Intelibill.Infrastructure.Billing;

internal sealed class TrialActivationConsentStore(ApplicationDbContext db, ShopAffiliationConsentStore authority,
    [FromKeyedServices(ShopTrialsV1.DeliverySection)] TimeProvider clock)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal Task<ErrorOr<int>> CountMembersAsync(MerchantBillingCaller caller, Guid shop, CancellationToken ct) =>
        WithOwnerAsync<int>(caller, shop, async (_, token) => await CountAsync(shop, token), ct);

    internal Task<ErrorOr<ShopTrialOperationV1>> AcceptAsync(MerchantBillingCaller caller, Guid shop,
        ShopTrialAcceptanceV1? request, CancellationToken ct) =>
        WithOwnerAsync<ShopTrialOperationV1>(caller, shop, (owner, token) => RecordAsync(shop, owner, request, token), ct);

    internal Task<ErrorOr<ShopTrialOperationV1>> ReadAsync(MerchantBillingCaller caller, Guid shop, Guid key, CancellationToken ct) =>
        WithOwnerAsync<ShopTrialOperationV1>(caller, shop, async (owner, token) =>
            await FindAsync(shop, owner.ActorId, key, token) is { } row
                ? ToResult(row) : Error.NotFound("Trial.OperationNotFound", "The trial operation was not found."), ct);

    private async Task<ErrorOr<T>> WithOwnerAsync<T>(MerchantBillingCaller caller, Guid shop,
        Func<ShopAffiliationConsentStore.LiveOwner, CancellationToken, Task<ErrorOr<T>>> action, CancellationToken ct)
    {
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", ct);
            if (await authority.LockOwnerAsync(caller, shop, ct) is not { } owner)
                return Error.Forbidden("Trial.OwnerRequired", "Only the active shop's owner can start or recover its trial.");
            var result = await action(owner, ct);
            if (!result.IsError)
                await tx.CommitAsync(ct);
            return result;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return MerchantBillingErrors.Unavailable;
        }
    }

    private async Task<ErrorOr<ShopTrialOperationV1>> RecordAsync(Guid shop, ShopAffiliationConsentStore.LiveOwner owner,
        ShopTrialAcceptanceV1? request, CancellationToken ct)
    {
        if (!ShopTrialsV1.Valid(request))
            return Error.Validation("Trial.AcceptanceRequired", "Confirm the exact trial terms to start a trial.");
        var accepted = request!;
        if (await FindAsync(shop, owner.ActorId, accepted.OperationKey, ct) is { } existing)
            return Consent(existing).Acceptance == accepted ? ToResult(existing) : MerchantBillingErrors.PayloadMismatch;
        if (await HasPendingAsync(shop, ct))
            return Error.Conflict("Trial.OperationPending", "A trial activation is already pending for this shop.");

        var now = clock.GetUtcNow();
        var at = new DateTimeOffset(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
        var members = await CountAsync(shop, ct);
        var consent = new ShopTrialConsentV1(Guid.NewGuid(), shop, owner.ActorId, owner.MembershipId, owner.Version,
            members, at, accepted);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.shop_trial_consents
                (event_id, shop_id, actor_id, membership_id, authority_version, operation_key, accepted_at, active_members, payload)
            VALUES ({consent.EventId}, {shop}, {owner.ActorId}, {owner.MembershipId}, {owner.Version}, {accepted.OperationKey},
                {at}, {members}, {JsonSerializer.Serialize(consent, Json)}::jsonb)
            """, ct);
        return new ShopTrialOperationV1(accepted.OperationKey, "Pending", null, at, at.AddDays(ShopTrialsV1.DurationDays));
    }

    private Task<bool> HasPendingAsync(Guid shop, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM public.shop_trial_consents c
                WHERE c.shop_id = {shop} AND NOT EXISTS (
                    SELECT FROM public.shop_trial_receipts r WHERE r.event_id = c.event_id)) AS "Value"
            """).SingleAsync(ct);

    private Task<int> CountAsync(Guid shop, CancellationToken ct) =>
        db.ShopMemberships.CountAsync(member => member.ShopId == shop && member.Status == MembershipStatus.Active, ct);

    private Task<ConsentRow?> FindAsync(Guid shop, Guid actor, Guid operation, CancellationToken ct) =>
        db.Database.SqlQuery<ConsentRow>($"""
            SELECT c.payload::text AS payload, r.status, r.reason FROM public.shop_trial_consents c
            LEFT JOIN public.shop_trial_receipts r ON r.event_id = c.event_id
            WHERE c.shop_id = {shop} AND c.actor_id = {actor} AND c.operation_key = {operation}
            """).SingleOrDefaultAsync(ct);

    private static ShopTrialConsentV1 Consent(ConsentRow row) => JsonSerializer.Deserialize<ShopTrialConsentV1>(row.Payload, Json)!;

    private static ShopTrialOperationV1 ToResult(ConsentRow row)
    {
        var consent = Consent(row);
        return new(consent.Acceptance.OperationKey, row.Status ?? "Pending", row.Reason,
            consent.AcceptedAt, consent.AcceptedAt.AddDays(ShopTrialsV1.DurationDays));
    }

    private sealed record ConsentRow(string Payload, string? Status, string? Reason);
}
