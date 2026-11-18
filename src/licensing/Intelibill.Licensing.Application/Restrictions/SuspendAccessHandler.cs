using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Status;
using Intelibill.Licensing.Domain.Entities;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Restrictions;

public sealed class SuspendAccessHandler(ISuspensionStore store, ISubscriptionStatusStore transactions,
    TimeProvider lifecycleClock, TimeProvider sessionClock)
{
    public Task<ErrorOr<SuspensionImpact>> PreviewAsync(AdminSessionCaller caller, Guid account,
        SuspensionSelection? selection, CancellationToken ct) => InScopeAsync<SuspensionImpact>(caller, account, true, false,
        async token =>
        {
            if (selection is not { IsValid: true }) return SuspensionErrors.Invalid;
            await store.LockAccountAsync(account, token);
            return await ReadImpactAsync(account, selection, token);
        }, ct);

    public Task<ErrorOr<SuspensionOutcome>> SuspendAsync(AdminSessionCaller caller, Guid account,
        SuspendAccessCommand? command, CancellationToken ct) => InScopeAsync<SuspensionOutcome>(caller, account, true, true,
        async token =>
        {
            if (command is null || !SuspensionInput.Valid(command)) return SuspensionErrors.Invalid;
            await store.LockAccountAsync(account, token);
            if (await AuthorizeAsync(caller, true, true, token) is { } expired) return expired;
            return await SaveAsync(caller, account, command, token);
        }, ct);

    public Task<ErrorOr<SuspensionStatus>> ReadAsync(AdminSessionCaller caller, Guid account, Guid id, CancellationToken ct) =>
        InScopeAsync<SuspensionStatus>(caller, account, false, false, async token =>
            await store.ReadAsync(account, id, token) is { } saved ? await StatusAsync(saved, token) : SuspensionErrors.Missing, ct);

    public Task<ErrorOr<SuspensionStatus[]>> ListAsync(AdminSessionCaller caller, Guid account, CancellationToken ct) =>
        InScopeAsync<SuspensionStatus[]>(caller, account, false, false, async token =>
        {
            if (await store.ReadAccountNameAsync(account, token) is null) return SuspensionErrors.Missing;
            var results = new List<SuspensionStatus>();
            foreach (var saved in await store.ListAsync(account, token))
                results.Add(await StatusAsync(saved, token));
            return results.ToArray();
        }, ct);

    private async Task<ErrorOr<SuspensionOutcome>> SaveAsync(AdminSessionCaller caller, Guid account,
        SuspendAccessCommand command, CancellationToken ct)
    {
        var digest = Hash(command);
        if (await store.ReadOperationAsync(account, command.OperationKey, ct) is { } prior)
            return prior.ActorId == caller.AdminId && prior.PayloadDigest == digest
                ? new SuspensionOutcome(await StatusAsync(prior, ct), false) : SuspensionErrors.PayloadMismatch;

        var impact = await ReadImpactAsync(account, new(command.Scope, command.ShopId), ct);
        if (impact.IsError) return impact.Errors;
        if (impact.Value.Version != command.ExpectedVersion || impact.Value.ImpactDigest != command.ImpactDigest)
            return SuspensionErrors.Conflict;
        if (impact.Value.AlreadySuspended) return SuspensionErrors.AlreadySuspended;

        var now = EntitlementSnapshotsV1.Instant(lifecycleClock);
        var effective = command.EffectiveAt?.ToUniversalTime() ?? now;
        if (effective < now) return SuspensionErrors.Invalid;
        var saved = new SavedSuspension(Guid.NewGuid(), account, command.ShopId, impact.Value.Version + 1, command.Mode!,
            effective, command.CustomerExplanation!.Trim(), command.InternalReason!.Trim(), command.ReviewReference!.Trim(),
            caller.AdminId, caller.SessionId, now, command.OperationKey, digest);
        await store.SaveAsync(saved, impact.Value.Shops, ct);
        if (await AuthorizeAsync(caller, true, true, ct) is { } expired) return expired;
        return new SuspensionOutcome(await StatusAsync(saved, ct), true);
    }

    private async Task<ErrorOr<SuspensionImpact>> ReadImpactAsync(Guid account, SuspensionSelection selection, CancellationToken ct)
    {
        var name = await store.ReadAccountNameAsync(account, ct);
        if (name is null) return SuspensionErrors.Missing;
        var shops = await store.ReadShopsAsync(account, selection.ShopId, ct);
        if (selection.ShopId is not null && shops.Count != 1) return SuspensionErrors.Missing;
        var (version, active) = await store.ReadVersionAsync(account, selection.ShopId, ct);
        var digest = Hash(new { account, name, selection, version, active, shops });
        return new SuspensionImpact(account, name, selection, version, active, shops, digest);
    }

    private async Task<SuspensionStatus> StatusAsync(SavedSuspension saved, CancellationToken ct)
    {
        var shops = await store.ReadDeliveryAsync(saved.Id, ct);
        var propagation = shops.Count == 0 ? "NoShops"
            : shops.Any(shop => shop.Status == "NeedsReview") ? "NeedsReview"
            : shops.All(shop => shop.Status == "Acknowledged") ? "Acknowledged" : "Pending";
        return new SuspensionStatus(saved.Id, saved.AccountId, saved.ShopId, saved.Version, saved.Mode,
            saved.EffectiveAt, saved.CustomerExplanation, saved.RecordedAt, propagation, shops);
    }

    private Task<ErrorOr<T>> InScopeAsync<T>(AdminSessionCaller caller, Guid account, bool write, bool recentMfa,
        Func<CancellationToken, Task<ErrorOr<T>>> operation, CancellationToken ct) =>
        transactions.InProviderTransactionAsync<T>(async token =>
        {
            if (await AuthorizeAsync(caller, write, recentMfa, token) is { } denied) return denied;
            await transactions.UseAccountAsync(account, token);
            return await operation(token);
        }, ct);

    private async Task<Error?> AuthorizeAsync(AdminSessionCaller caller, bool write, bool recentMfa, CancellationToken ct)
    {
        var (actor, session) = await transactions.LockProviderAsync(caller, ct);
        var now = sessionClock.GetUtcNow();
        if (session is null || now >= caller.AccessExpiresAt || !session.IsLiveAt(now) ||
            actor is not { DeactivatedAt: null, MfaEnrolledAt: not null })
            return AdminRefreshErrors.SessionEnded;
        if (!actor.Capabilities.HasFlag(AdminCapability.SecurityAdministrator) &&
            (write || !actor.Capabilities.HasFlag(AdminCapability.Support)))
            return SuspensionErrors.Denied;
        if (recentMfa && !new ProviderActor(caller.AdminId, session.MfaVerifiedAt).HasRecentMfa(now))
            return SuspensionErrors.RecentMfa;
        return null;
    }

    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
