using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Restrictions;

public sealed class ReviewReopeningHandler(IClosureStore closures, ISuspensionStore suspensions,
    ISubscriptionAccessFactsReader terms, ISubscriptionStatusStore scope, TimeProvider lifecycleClock, TimeProvider sessionClock)
{
    public Task<ErrorOr<ReopeningImpact>> PreviewAsync(AdminSessionCaller caller, Guid account, Guid closure, Guid shop,
        CancellationToken ct) => InScopeAsync<ReopeningImpact>(caller, account, false, async (_, token) =>
    {
        var target = await closures.ReadReopeningTargetAsync(account, closure, shop, token);
        if (target is null) return ReopeningErrors.Missing;
        if (target.Reopened) return ReopeningErrors.Reopened;
        return await ImpactAsync(account, target, token);
    }, ct);

    public Task<ErrorOr<ReopeningOutcome>> ReviewAsync(AdminSessionCaller caller, Guid account, Guid closure, Guid shop,
        ReviewReopeningCommand? command, CancellationToken ct) =>
        InScopeAsync<ReopeningOutcome>(caller, account, true, async (mfaAt, token) =>
            command is not { } valid || !valid.IsValid() ? ReopeningErrors.Invalid
                : await SaveAsync(caller, account, closure, shop, valid, mfaAt, token), ct);

    private async Task<ErrorOr<ReopeningOutcome>> SaveAsync(AdminSessionCaller caller, Guid account, Guid closure, Guid shop,
        ReviewReopeningCommand command, DateTimeOffset mfaAt, CancellationToken ct)
    {
        var digest = SuspendAccessHandler.Hash(new { action = "ReopenShop", account, closure, shop, command });
        if (await closures.ReadReopeningOperationAsync(account, command.OperationKey, ct) is { } prior)
            return prior.ReopenedBy == caller.AdminId && prior.PayloadDigest == digest
                ? new ReopeningOutcome(prior.Decision, false) : ReopeningErrors.PayloadMismatch;

        var target = await closures.ReadReopeningTargetAsync(account, closure, shop, ct);
        if (target is null) return ReopeningErrors.Missing;
        if (target.Reopened) return ReopeningErrors.Reopened;
        var impact = await ImpactAsync(account, target, ct);
        if (impact.Version != command.ExpectedVersion || impact.ImpactDigest != command.ImpactDigest)
            return ReopeningErrors.Conflict;
        if (!impact.CoverageAdequate || impact.Coverage is not { } coverage) return ReopeningErrors.Coverage;
        var recordedAt = EntitlementSnapshotsV1.Instant(sessionClock);
        if (command.OwnerConsentAt <= target.ClosedAt || command.OwnerConsentAt > recordedAt ||
            await closures.UsedReopeningConsentAsync(account, shop, command.OwnerConsentReference!.Trim(), ct))
            return ReopeningErrors.Consent;
        if (coverage.PaidThrough <= EntitlementSnapshotsV1.Instant(lifecycleClock))
            return ReopeningErrors.Coverage;

        var decision = new ReopeningDecision(Guid.NewGuid(), closure, shop, target.Version + 1, recordedAt);
        await closures.SaveReopeningAsync(new(decision, account, target.AffiliationRequestId, caller.AdminId,
            caller.SessionId, mfaAt, impact.EvaluatedAt, digest, command, coverage), ct);
        var authority = await AuthorizeAsync(caller, true, ct);
        return authority.IsError ? authority.Errors : new ReopeningOutcome(decision, true);
    }

    private async Task<ReopeningImpact> ImpactAsync(Guid account, ReopeningTarget target, CancellationToken ct)
    {
        var at = EntitlementSnapshotsV1.Instant(lifecycleClock);
        await scope.UseShopAsync(target.ShopId, ct);
        var coverage = SubscriptionStatusProjection.Choose(await terms.ReadShopAsync(target.ShopId, ct, at), at, commercial: false);
        var adequate = coverage is { State: "Active", EvidenceState: "Complete", Basis: not (null or "Trial") } &&
            coverage.AccessEndsAt > at;
        var surviving = await suspensions.ReadSurvivingAsync(account, target.ShopId, Guid.Empty, at, ct);
        var restriction = surviving is null ? null : new ReleaseRestrictionImpact(surviving.Mode, surviving.EffectiveAt);
        var accountRestriction = await suspensions.ReadVersionAsync(account, null, ct);
        var shopRestriction = await suspensions.ReadVersionAsync(account, target.ShopId, ct);
        var digest = SuspendAccessHandler.Hash(new
        {
            account, target, coverage, surviving?.Id, restriction,
            accountRestriction.Version, ShopRestrictionVersion = shopRestriction.Version,
        });
        return new(target.ClosureId, target.ClosureScope, target.ClosedAt, target.ShopId, target.ShopName,
            target.AffiliationRequestId, target.Version, at, coverage, adequate, target.OtherActiveClosures, restriction, digest);
    }

    private Task<ErrorOr<T>> InScopeAsync<T>(AdminSessionCaller caller, Guid account, bool write,
        Func<DateTimeOffset, CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct) =>
        scope.InProviderTransactionAsync<T>(async token =>
        {
            var authority = await AuthorizeAsync(caller, write, token);
            if (authority.IsError) return authority.Errors;
            await scope.UseAccountAsync(account, token);
            await closures.LockAccountAsync(account, token);
            authority = await AuthorizeAsync(caller, write, token);
            return authority.IsError ? authority.Errors : await operation(authority.Value, token);
        }, ct);

    private async Task<ErrorOr<DateTimeOffset>> AuthorizeAsync(AdminSessionCaller caller, bool recentMfa, CancellationToken ct)
    {
        var (actor, session) = await scope.LockProviderAsync(caller, ct);
        var now = sessionClock.GetUtcNow();
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now) ||
            actor is not { DeactivatedAt: null, MfaEnrolledAt: not null })
            return AdminRefreshErrors.SessionEnded;
        if (!actor.Capabilities.HasFlag(AdminCapability.LicensingManager)) return ReopeningErrors.Denied;
        if (recentMfa && !new ProviderActor(caller.AdminId, session.MfaVerifiedAt).HasRecentMfa(now))
            return ReopeningErrors.RecentMfa;
        return session.MfaVerifiedAt;
    }
}
