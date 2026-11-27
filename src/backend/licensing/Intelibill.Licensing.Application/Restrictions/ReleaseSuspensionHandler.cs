using ErrorOr;
using Intelibill.Licensing.Application.AdminIdentities;
using Intelibill.Licensing.Application.Status;
using Intelibill.Shared.Contracts.V1;

namespace Intelibill.Licensing.Application.Restrictions;

public sealed class ReleaseSuspensionHandler(ISuspensionStore store, SuspendAccessHandler access,
    ISubscriptionAccessFactsReader terms, ISubscriptionStatusStore scope, TimeProvider clock)
{
    public Task<ErrorOr<ReleaseImpact>> PreviewAsync(AdminSessionCaller caller, Guid account, Guid id, CancellationToken ct) =>
        access.InScopeAsync<ReleaseImpact>(caller, account, true, false, async token =>
        {
            await store.LockAccountAsync(account, token);
            var saved = await store.ReadAsync(account, id, token);
            if (saved is null) return SuspensionErrors.Missing;
            if (await store.ReadReleaseAsync(account, id, token) is not null) return SuspensionErrors.Conflict;
            return await ImpactAsync(saved, token);
        }, ct);

    public Task<ErrorOr<SuspensionOutcome>> ReleaseAsync(AdminSessionCaller caller, Guid account, Guid id,
        ReleaseSuspensionCommand? command, CancellationToken ct) =>
        access.InScopeAsync<SuspensionOutcome>(caller, account, true, true, async token =>
        {
            if (command is null || !SuspensionInput.Valid(command)) return SuspensionErrors.InvalidRelease;
            await store.LockAccountAsync(account, token);
            var proof = await access.AuthorizeAsync(caller, true, true, token);
            if (proof.IsError) return proof.Errors;
            return await SaveAsync(caller, account, id, command, proof.Value, token);
        }, ct);

    private async Task<ErrorOr<SuspensionOutcome>> SaveAsync(AdminSessionCaller caller, Guid account, Guid id,
        ReleaseSuspensionCommand command, DateTimeOffset mfaAt, CancellationToken ct)
    {
        var saved = await store.ReadAsync(account, id, ct);
        if (saved is null) return SuspensionErrors.Missing;
        var digest = SuspendAccessHandler.Hash(new { id, command });
        if (await store.ReadReleaseOperationAsync(account, command.OperationKey, ct) is { } prior)
            return prior.ReleasedBy == caller.AdminId && prior.PayloadDigest == digest
                ? new SuspensionOutcome(await access.StatusAsync(saved, ct), false) : SuspensionErrors.PayloadMismatch;

        if (saved.Version != command.ExpectedVersion || await store.ReadReleaseAsync(account, id, ct) is not null)
            return SuspensionErrors.Conflict;
        var impact = await ImpactAsync(saved, ct);
        if (impact.ImpactDigest != command.ImpactDigest || impact.EvaluatedAt < saved.RecordedAt)
            return SuspensionErrors.Conflict;

        await store.ReleaseAsync(saved, new SavedRelease(Guid.NewGuid(), id, account, saved.Version + 1,
            impact.EvaluatedAt, caller.AdminId, caller.SessionId, mfaAt, command.Reason!.Trim(),
            command.OperationKey, digest), impact.Shops, ct);
        var authority = await access.AuthorizeAsync(caller, true, true, ct);
        if (authority.IsError) return authority.Errors;
        return new SuspensionOutcome(await access.StatusAsync(saved, ct), true);
    }

    private async Task<ReleaseImpact> ImpactAsync(SavedSuspension saved, CancellationToken ct)
    {
        var at = EntitlementSnapshotsV1.Instant(clock);
        var shops = new List<ReleaseShopImpact>();
        var survivingIds = new List<Guid?>();
        foreach (var shop in await store.ReadShopsAsync(saved.AccountId, saved.ShopId, ct))
        {
            await scope.UseShopAsync(shop.ShopId, ct);
            var coverage = await terms.ReadShopAsync(shop.ShopId, ct, at);
            var term = SubscriptionStatusProjection.Choose(coverage, at, commercial: false);
            var surviving = await store.ReadSurvivingAsync(saved.AccountId, shop.ShopId, saved.Id, at, ct);
            survivingIds.Add(surviving?.Id);
            var restriction = surviving is null ? null : new ReleaseRestrictionImpact(surviving.Mode, surviving.EffectiveAt);
            shops.Add(new(shop.ShopId, shop.ShopName, shop.AffiliationId, term?.State ?? "PendingActivation",
                term?.TermId, term?.Basis, term?.StartsAt, term?.PaidThrough, term?.GraceEndsAt, term?.AccessEndsAt,
                term?.MemberAllowance, restriction));
        }
        var digest = SuspendAccessHandler.Hash(new { saved.Id, saved.Version, shops, survivingIds });
        return new(saved.Id, saved.Version, at, shops, digest);
    }
}
