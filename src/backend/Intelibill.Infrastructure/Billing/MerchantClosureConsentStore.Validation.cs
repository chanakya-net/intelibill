using System.Text.Json;
using ErrorOr;
using Intelibill.Application.Common.Licensing;
using Intelibill.Domain.Entities;
using Intelibill.Domain.Enums;
using Intelibill.Shared.Contracts.V1;
using Intelibill.Shared.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Intelibill.Infrastructure.Billing;

public interface IMerchantClosureValidationReceiver
{
    Task<ErrorOr<ClosureValidatedV1>> ValidateAsync(ClosureValidationV1 request, VerifiedServiceAssertion proof, CancellationToken ct);
}

internal sealed partial class MerchantClosureConsentStore
{
    public async Task<ErrorOr<ClosureValidatedV1>> ValidateAsync(ClosureValidationV1 request,
        VerifiedServiceAssertion proof, CancellationToken ct)
    {
        if (!ValidScope(request) || request.ExpiresAt > proof.ExpiresAt || request.ExpiresAt <= clock.GetUtcNow())
            return Stale;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'", ct);
            var entries = await LockTargetsAsync(request, ct);
            if (entries.IsError)
                return entries.Errors;
            await memberships.LockUsersAsync(entries.Value.Select(entry => entry.Consent.ActorId).ToArray(), ct);
            var results = await ValidateTargetsAsync(request, entries.Value, ct);
            if (results.IsError)
                return results.Errors;
            if (!await ConsumeValidationAsync(proof, ct) || clock.GetUtcNow() >= request.ExpiresAt)
                return Stale;
            await transaction.CommitAsync(ct);
            return new ClosureValidatedV1(request.AccountId, request.ProposalId, request.Challenge, request.ScopeDigest, results.Value);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return Error.Failure("Closure.MerchantUnavailable", "The merchant service could not validate closure.");
        }
    }

    private async Task<ErrorOr<ValidationEntry[]>> LockTargetsAsync(ClosureValidationV1 request, CancellationToken ct)
    {
        var entries = new List<ValidationEntry>(request.Targets.Length);
        foreach (var target in request.Targets.OrderBy(target => target.ShopId))
        {
            var locked = await fence.EnterProjectionAsync(request.AccountId, target.ShopId, ct);
            if (locked.IsError) return locked.Errors;
            await SetShopAsync(target.ShopId, ct);
            var row = await ConsentRows(target.ShopId, request.AccountId, request.ProposalId)
                .Where(row => row.AffiliationId == target.AffiliationRequestId && row.ScopeDigest == request.ScopeDigest &&
                    row.ClosureVersion == target.ClosureVersion && row.IsAccountClosure == (request.RequiredActor == null) &&
                    (request.RequiredActor == null || row.ActorId == request.RequiredActor))
                .OrderByDescending(row => row.RecordedAt).ThenByDescending(row => row.Id).FirstOrDefaultAsync(ct);
            if (row is null) return Stale;
            entries.Add(new ValidationEntry(target, row));
        }
        return entries.ToArray();
    }

    private async Task<ErrorOr<ClosureValidatedTargetV1[]>> ValidateTargetsAsync(ClosureValidationV1 request,
        ValidationEntry[] entries, CancellationToken ct)
    {
        var results = new List<ClosureValidatedTargetV1>(entries.Length);
        var leases = new List<ShopLicenceLease>(entries.Length);
        foreach (var entry in entries)
        {
            var (target, consent) = entry;
            await SetShopAsync(target.ShopId, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                SELECT set_config('app.current_user_id', {consent.ActorId.ToString("D")}, true)
                """, ct);
            var lease = await fence.EnterAsync(target.ShopId, LicenceAction.History, ct);
            if (lease.IsError) return lease.Errors;
            if (!await memberships.LockShopAsync(target.ShopId, ct)) return Stale;
            var owner = await memberships.LockMembershipAsync(target.ShopId, consent.ActorId, ct);
            if (owner is not { Role: ShopRole.Owner, Status: MembershipStatus.Active } ||
                owner.Id != consent.MembershipId || owner.AuthorityVersion != consent.OwnerVersion ||
                !owner.User.IsLoginEnabled || !(owner.User.IsEmailVerified || owner.User.IsPhoneVerified))
                return Stale;
            var binding = await db.Set<ShopLicenseState>().AsNoTracking().SingleOrDefaultAsync(row => row.ShopId == target.ShopId, ct);
            if (binding?.AccountId != request.AccountId || binding.AffiliationRequestId != target.AffiliationRequestId ||
                lease.Value.CurrentDecision() is { Enforced: true, Denial: not null })
                return Stale;
            if (!request.Recovering && !await MatchesWorkAsync(binding, owner, entry, ct))
                return Stale;
            leases.Add(lease.Value);
            results.Add(new(target.ShopId, consent.Id, consent.WorkDigest));
        }
        return leases.Any(lease => lease.CurrentDecision() is { Enforced: true, Denial: not null })
            ? Stale : results.ToArray();
    }

    private async Task<bool> MatchesWorkAsync(ShopLicenseState binding, ShopMembership owner, ValidationEntry entry,
        CancellationToken ct)
    {
        var review = await ReadReviewAsync(new(binding, new(owner.UserId, owner.Id, owner.AuthorityVersion,
            entry.Target.ShopName, string.Empty)), ct);
        return review.ClosureVersion == entry.Target.ClosureVersion && review.WorkDigest == entry.Consent.WorkDigest;
    }

    private Task<int> SetShopAsync(Guid shop, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.active_shop_id', {shop.ToString("D")}, true)", ct);

    private async Task<bool> ConsumeValidationAsync(VerifiedServiceAssertion proof, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT set_config('app.entitlement_replay_id', {proof.ReplayId.ToString("D")}, true)
            """, ct);
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public.entitlement_request_uses (replay_id, expires_at) VALUES ({proof.ReplayId}, {proof.ExpiresAt})
            ON CONFLICT DO NOTHING
            """, ct) == 1;
    }

    private static bool ValidScope(ClosureValidationV1 request) =>
        request.AccountId != Guid.Empty && request.ProposalId != Guid.Empty && request.Challenge != Guid.Empty &&
        request.Targets is { Length: > 0 and <= ShopClosuresV1.MaxTargets } targets &&
        targets.All(row => row.ShopId != Guid.Empty && row.AffiliationRequestId != Guid.Empty && row.ClosureVersion is 0 or 1) &&
        targets.Select(row => row.ShopId).Distinct().Count() == targets.Length &&
        request.ScopeDigest == SignedServiceAssertion.Digest(JsonSerializer.SerializeToUtf8Bytes(targets, Json));

    private sealed record ValidationEntry(ClosureTargetV1 Target, ConsentRow Consent);
}
